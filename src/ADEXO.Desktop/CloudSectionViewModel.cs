using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ADEXO.Contracts.Authentication;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Results;
using ADEXO.Core.Correlation;
using ADEXO.Graph.Authentication;
using ADEXO.Graph.Orchestration;

namespace ADEXO.Desktop;

/// <summary>
/// The read-only Microsoft 365 section.
/// <para>
/// It is deliberately independent of the Active Directory view: it holds its own state, never mutates
/// AD state, and a failure here cannot unload the AD user. There is no write command of any kind, and
/// none of the displayed values is editable.
/// </para>
/// </summary>
public sealed class CloudSectionViewModel : INotifyPropertyChanged
{
    private readonly IMicrosoftAuthenticationService? _authentication;
    private readonly CloudReadSession? _session;
    private readonly RelayCommand _signInCommand;
    private readonly RelayCommand _signOutCommand;
    private readonly RelayCommand _refreshCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly Func<byte[]?> _consistencyGuidProvider = () => null;
    private readonly Func<AdCorrelationSubject?>? _subjectProvider;
    private CancellationTokenSource? _attemptCancellation;

    private CloudSectionState _state = CloudSectionState.NotConfigured;
    private string _message = "Microsoft 365 is not configured.";
    private AuthenticatedAccount? _account;
    private CloudDirectoryUser? _user;
    private TenantVerificationResult? _tenant;
    private string? _correlationSummary;
    private bool _busy;

    public CloudSectionViewModel()
    {
        _signInCommand = new RelayCommand(() => { }, () => false);
        _signOutCommand = new RelayCommand(() => { }, () => false);
        _refreshCommand = new RelayCommand(() => { }, () => false);
        _cancelCommand = new RelayCommand(() => { }, () => false);
    }

    /// <summary>
    /// Builds a section that reports why it is unavailable. The reason is a sanitized explanation of
    /// the configuration problem, never a credential.
    /// </summary>
    public static CloudSectionViewModel NotConfigured(string reason)
    {
        var section = new CloudSectionViewModel
        {
            State = CloudSectionState.NotConfigured,
            Message = reason,
        };

        return section;
    }

    public CloudSectionViewModel(
        IMicrosoftAuthenticationService authentication,
        CloudReadSession session,
        Func<AdCorrelationSubject?> subjectProvider,
        Func<IntPtr> ownerWindowHandleProvider,
        Func<byte[]?>? consistencyGuidProvider = null)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(subjectProvider);
        ArgumentNullException.ThrowIfNull(ownerWindowHandleProvider);
        _consistencyGuidProvider = consistencyGuidProvider ?? (() => null);
        _subjectProvider = subjectProvider;

        _signInCommand = new RelayCommand(
            async () => await SignInAsync(ownerWindowHandleProvider()).ConfigureAwait(true),
            () => !_busy);
        _signOutCommand = new RelayCommand(
            async () => await SignOutAsync().ConfigureAwait(true),
            () => !_busy && _account is not null);

        // A sign-in can wait indefinitely on a browser window the operator has since closed. Without a
        // way to abandon it, every command stays disabled and the application has to be killed, so the
        // cancel command is available exactly while an attempt is in flight.
        _cancelCommand = new RelayCommand(CancelAttempt, () => _busy);

        _refreshCommand = new RelayCommand(
            async () => await RefreshAsync(subjectProvider()).ConfigureAwait(true),
            () => !_busy && _account is not null);

        // A configured section starts signed out, not "not configured". Leaving the field default
        // would report the section as unavailable until the operator did something, which reads as a
        // configuration failure that is not there.
        State = CloudSectionState.SignedOut;
        Message = StateText(CloudSectionState.SignedOut);

        // The fallback is announced while the browser is opening, not after it has closed, so the
        // administrator knows why a browser window appeared.
        if (authentication is PublicClientAuthenticationService service)
        {
            service.BrowserFallbackStarting += OnBrowserFallbackStarting;
        }
    }

    private void OnBrowserFallbackStarting(object? sender, FallbackReason reason)
    {
        ReportBrowserFallback();

        // The diagnostics are the evidence an administrator reads while the browser is open, so they are
        // refreshed at the moment the fallback is decided rather than only after the attempt resolves.
        MethodChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raised when the authentication method or fallback reason changes without a new cloud result, such
    /// as when the browser fallback begins. Diagnostics rebuild from it.
    /// </summary>
    public event EventHandler? MethodChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised after a sign-in, refresh, or sign-out changes the cloud result, so the product sections can
    /// be rebuilt from it. Without this the tabs keep the cloud state they had when they were created,
    /// which is how a completed sign-in left the Microsoft 365 tab reporting "Disconnected".
    /// </summary>
    public event EventHandler<CloudSectionResult?>? ResultChanged;

    /// <summary>Exposes the last applied cloud result so the product shell can rebuild from it.</summary>
    public CloudSectionResult? LastResult { get; private set; }

    public ICommand SignInCommand => _signInCommand;

    public ICommand SignOutCommand => _signOutCommand;

    public ICommand RefreshCommand => _refreshCommand;

    /// <summary>Abandons an in-flight sign-in. Available only while one is running.</summary>
    public ICommand CancelCommand => _cancelCommand;

    public CloudSectionState State
    {
        get => _state;
        private set => Set(ref _state, value);
    }

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public bool IsConfigured => _authentication is not null;

    /// <summary>The signed-in administrator, for display. Never a token.</summary>
    public string? SignedInAs => _account?.Username;

    /// <summary>The tenant, shown shortened so the whole identifier does not dominate the panel.</summary>
    public string? TenantDisplay => _tenant?.ApprovedTenantId.ToString("D")[..8] is { Length: > 0 } prefix
        ? prefix
        : null;

    /// <summary>True when a cloud user may be presented as belonging to the selected AD object.</summary>
    public bool ShowUserData => State == CloudSectionState.Verified && _user is not null;

    /// <summary>
    /// The verified Entra object identifier, or null. This is the only value a downstream provider may
    /// use to resolve a cloud-side object, and it is null unless correlation is Verified.
    /// </summary>
    public Guid? VerifiedEntraObjectId => _user?.EntraObjectId;
    public string? EntraObjectId => ShowUserData ? _user!.EntraObjectId.ToString("D") : null;

    public string? CloudUserPrincipalName => ShowUserData ? _user!.UserPrincipalName : null;

    public string? CloudMail => ShowUserData ? _user!.Mail : null;

    public string? SyncEnabledDisplay => !ShowUserData
        ? null
        : _user!.OnPremisesSyncEnabled is null
            ? "Not synchronized"
            : _user.OnPremisesSyncEnabled.Value ? "Synchronized from Active Directory" : "Cloud only";

    public string? LastSyncDisplay => ShowUserData && _user!.OnPremisesLastSyncDateTime is { } last
        ? last.ToUniversalTime().ToString("u")
        : null;

    public string? UsageLocationDisplay => ShowUserData ? _user!.UsageLocation : null;

    /// <summary>
    /// Licence labels. When the catalogue could not be read, the identifiers are shown and the panel
    /// says the names are unavailable, rather than implying the user holds no licences.
    /// </summary>
    public string LicenseSummary
    {
        get
        {
            if (!ShowUserData)
            {
                return string.Empty;
            }

            if (_user!.Licenses.Count == 0)
            {
                return "No licences assigned.";
            }

            var labels = _user.Licenses.Select(license => license.Label);
            var text = string.Join(", ", labels);

            return _user.LicenseCatalogueResolved
                ? text
                : $"{text} (licence names unavailable)";
        }
    }

    public string? CorrelationSummary => ShowUserData ? null : _correlationSummary;

    /// <summary>
    /// Completes sign-in and both tenant locks. No user data is displayed by this step, because no
    /// correlation has run yet.
    /// </summary>
    public async Task SignInAsync(IntPtr ownerWindowHandle)
    {
        if (_authentication is null || _session is null)
        {
            return;
        }

        SetBusy(true);
        State = CloudSectionState.SigningIn;

        // The wording names the mechanism that is about to be attempted, so the administrator is not
        // surprised when Windows sign-in appears, or when a browser opens instead.
        Message = SigningInMessage();

        // A sign-in that produces no dialog and no result used to leave the section in SigningIn with
        // nothing to inspect. The watchdog reports progress after a threshold; it never cancels a
        // dialog the operator may still be using for MFA.
        using var watchdog = new AuthenticationWatchdog(this);
        watchdog.Start();

        // A token lets the operator abandon an attempt that will never complete, such as a browser
        // window they have closed.
        using var attemptCancellation = new CancellationTokenSource();
        _attemptCancellation = attemptCancellation;

        try
        {
            var signIn = await _authentication.SignInAsync(ownerWindowHandle, attemptCancellation.Token).ConfigureAwait(true);

            if (signIn.IsCanceled)
            {
                State = CloudSectionState.Canceled;
                Message = "The sign-in was canceled. Active Directory data is unaffected.";
                return;
            }

            if (!signIn.IsSuccess)
            {
                ApplyAuthenticationFailure(signIn.Error?.Category, signIn.Error?.Summary);
                return;
            }

            _account = signIn.Value;
            Message = DescribeMethod();

            var (tenant, result) = await _session.EstablishTenantAsync(attemptCancellation.Token).ConfigureAwait(true);
            _tenant = tenant;
            Apply(result);

            // Both locks passed, so the cloud read may now begin. Doing it here rather than waiting for a
            // second click is what makes a sign-in produce a populated view; the ordering is unchanged,
            // because CorrelateAsync refuses any tenant result that is not verified.
            if (result.State == CloudSectionState.TenantVerified && _subjectProvider?.Invoke() is { } subject)
            {
                var correlated = await _session
                    .CorrelateAsync(subject, tenant!, _consistencyGuidProvider(), attemptCancellation.Token)
                    .ConfigureAwait(true);
                var resolved = await _session.ResolveLicensesAsync(correlated, attemptCancellation.Token).ConfigureAwait(true);
                Apply(resolved);
            }
        }
        catch (OperationCanceledException)
        {
            // The operator abandoned the attempt, or closed the browser window. That is an ordinary way
            // to stop, not a failure, and it must leave the section usable rather than showing an error.
            State = CloudSectionState.Canceled;
            Message = "The sign-in was canceled. Active Directory data is unaffected.";
            ReportCancellation();
        }
        finally
        {
            _attemptCancellation = null;
            watchdog.Stop();
            SetBusy(false);
        }
    }

    /// <summary>
    /// Abandons the in-flight sign-in. The operator closing a browser window is an ordinary way to stop,
    /// and it must leave the application usable rather than with every command disabled.
    /// </summary>
    public void CancelAttempt()
    {
        if (_attemptCancellation is not { } cancellation || cancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The attempt completed between the check and the cancel; there is nothing left to stop.
            return;
        }

        State = CloudSectionState.Canceled;
        Message = "The sign-in was canceled. Active Directory data is unaffected.";
        ReportCancellation();
    }

    private void ReportCancellation()
    {
        LastResult = null;
        _user = null;
        _correlationSummary = null;
        ResultChanged?.Invoke(this, null);
    }

    public async Task SignOutAsync()
    {
        if (_authentication is null)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await _authentication.SignOutAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            _account = null;
            _tenant = null;
            _user = null;
            _correlationSummary = null;
            LastResult = null;
            State = CloudSectionState.SignedOut;
            Message = "Signed out of Microsoft 365. Active Directory data is unaffected.";
            OnPropertyChanged(nameof(ShowUserData));
            OnPropertyChanged(nameof(SignedInAs));
            OnPropertyChanged(nameof(TenantDisplay));
            OnPropertyChanged(nameof(EntraObjectId));
            OnPropertyChanged(nameof(CloudUserPrincipalName));
            OnPropertyChanged(nameof(CloudMail));
            OnPropertyChanged(nameof(SyncEnabledDisplay));
            OnPropertyChanged(nameof(LastSyncDisplay));
            OnPropertyChanged(nameof(UsageLocationDisplay));
            OnPropertyChanged(nameof(LicenseSummary));
            OnPropertyChanged(nameof(CorrelationSummary));

            // The tabs must stop showing cloud data the moment the session ends, or a signed-out window
            // would keep presenting a previous administrator's cloud view.
            ResultChanged?.Invoke(this, null);
            SetBusy(false);
        }
    }

    /// <summary>
    /// Re-runs the tenant lock and correlation for the current AD selection. Refresh never changes the
    /// AD identity; it only re-reads the cloud side for the identity that is already verified.
    /// </summary>
    public async Task RefreshAsync(AdCorrelationSubject? subject)
    {
        if (_session is null || _account is null)
        {
            return;
        }

        SetBusy(true);
        State = CloudSectionState.CorrelationPending;
        Message = "Reading Microsoft 365 state.";

        try
        {
            var (tenant, established) = await _session.EstablishTenantAsync(CancellationToken.None).ConfigureAwait(true);
            _tenant = tenant;

            // A failed lock must not proceed to a user read, and must clear any user data already shown.
            if (tenant is null || !tenant.IsVerified)
            {
                _user = null;
                Apply(established);
                return;
            }

            if (subject is null)
            {
                Apply(established);
                return;
            }

            var correlated = await _session.CorrelateAsync(subject, tenant, _consistencyGuidProvider(), CancellationToken.None).ConfigureAwait(true);
            var resolved = await _session.ResolveLicensesAsync(correlated, CancellationToken.None).ConfigureAwait(true);
            Apply(resolved);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Apply(CloudSectionResult result)
    {
        LastResult = result;
        State = result.State;
        Message = result.Message ?? StateText(result.State);
        _correlationSummary = result.Correlation?.Summary;
        _user = result.ShowsUserData ? result.User : null;

        OnPropertyChanged(nameof(ShowUserData));
        OnPropertyChanged(nameof(SignedInAs));
        OnPropertyChanged(nameof(TenantDisplay));
        OnPropertyChanged(nameof(EntraObjectId));
        OnPropertyChanged(nameof(CloudUserPrincipalName));
        OnPropertyChanged(nameof(CloudMail));
        OnPropertyChanged(nameof(SyncEnabledDisplay));
        OnPropertyChanged(nameof(LastSyncDisplay));
        OnPropertyChanged(nameof(UsageLocationDisplay));
        OnPropertyChanged(nameof(LicenseSummary));
        OnPropertyChanged(nameof(CorrelationSummary));

        // The product tabs are built from this result. It must be announced, because a completed sign-in
        // that leaves them reporting "Disconnected" is indistinguishable from a failed one.
        ResultChanged?.Invoke(this, result);
    }

    private void ApplyAuthenticationFailure(FailureCategory? category, string? summary)
    {
        State = category switch
        {
            FailureCategory.Authorization => CloudSectionState.PermissionMissing,
            FailureCategory.Network or FailureCategory.ProviderUnavailable or FailureCategory.Timeout => CloudSectionState.GraphUnavailable,
            _ => CloudSectionState.Failed,
        };

        Message = summary ?? "The Microsoft 365 sign-in failed. Active Directory data is unaffected.";

        // A failure is an outcome too. The tabs must clear any previous cloud data rather than keep
        // showing a session that no longer exists.
        LastResult = null;
        _user = null;
        _correlationSummary = null;
        ResultChanged?.Invoke(this, null);
    }

    /// <summary>
    /// Appends a progress note to the status message. Called by the watchdog; it never changes the
    /// state or cancels anything.
    /// </summary>
    public void ReportProgress(string note)
    {
        Message = string.IsNullOrWhiteSpace(Message) ? note : $"{Message} {note}";
    }

    /// <summary>
    /// The progress wording for the mechanism that is about to be attempted. Auto names Windows first
    /// because that is the preferred path; the fallback announces itself only if it actually happens.
    /// </summary>
    private string SigningInMessage() => _authentication is PublicClientAuthenticationService service
        ? service.Mode switch
        {
            AuthenticationMode.SystemBrowserOnly => "Signing in with your browser...",
            AuthenticationMode.WamOnly => "Signing in with Windows...",
            _ => "Signing in with Windows...",
        }
        : "Signing in to Microsoft 365.";

    /// <summary>
    /// Appends a progress note when the bounded broker window has expired and the browser is starting.
    /// The operator needs to know why a browser window is about to appear.
    /// </summary>
    public void ReportBrowserFallback()
    {
        if (_authentication is PublicClientAuthenticationService service
            && service.Mode == AuthenticationMode.Auto)
        {
            Message = "Windows sign-in couldn't open. Continuing securely in your browser...";
        }
    }
    /// <summary>
    /// Describes which authentication mechanism was used, in the wording an administrator needs. A
    /// fallback is stated plainly rather than hidden, because it changes the security properties.
    /// </summary>
    private string DescribeMethod()
    {
        if (_authentication is not PublicClientAuthenticationService service)
        {
            return StateText(State);
        }

        return service.LastFallbackReason switch
        {
            FallbackReason.None when service.LastMethod == AuthenticationMethod.WindowsBroker =>
                "Signed in with Windows.",
            FallbackReason.None when service.LastMethod == AuthenticationMethod.SystemBrowser =>
                "Signed in with your browser.",
            FallbackReason.BrokerUiNotSurfaced or FallbackReason.BrokerTimeout =>
                "Windows sign-in couldn't open. Continuing securely in your browser.",
            FallbackReason.BrokerActivationFailure or FallbackReason.BrokerUnavailable =>
                "Windows sign-in was unavailable. Continuing securely in your browser.",
            FallbackReason.ExplicitUserFallback =>
                "Signed in with your browser.",
            _ => StateText(State),
        };
    }

    /// <summary>The configured mode, named for diagnostics.</summary>
    public string PreferredMethodText => _authentication is PublicClientAuthenticationService service
        ? service.Mode switch
        {
            AuthenticationMode.WamOnly => "Windows broker only",
            AuthenticationMode.SystemBrowserOnly => "System browser only",
            _ => "Windows broker, then browser",
        }
        : "—";

    /// <summary>What actually produced the current session.</summary>
    public string ActualMethodText => _authentication is PublicClientAuthenticationService service
        ? service.LastMethod switch
        {
            AuthenticationMethod.WindowsBroker => "Windows broker",
            AuthenticationMethod.SystemBrowser => "System browser",
            _ => _account is null ? "None yet" : "Cached account",
        }
        : "—";

    /// <summary>The fallback reason, phrased for a human.</summary>
    public string FallbackReasonText => _authentication is PublicClientAuthenticationService service
        ? service.LastFallbackReason switch
        {
            FallbackReason.None => "None",
            FallbackReason.BrokerUnavailable => "Broker unavailable",
            FallbackReason.BrokerActivationFailure => "Broker activation failed",
            FallbackReason.BrokerUiNotSurfaced => "Broker UI did not appear",
            FallbackReason.BrokerTimeout => "Broker timed out before showing UI",
            FallbackReason.ExplicitUserFallback => "Browser chosen explicitly",
            _ => "—",
        }
        : "—";

    /// <summary>True when the current session came from the browser because the broker could not be used.</summary>
    public bool HasFallback => _authentication is PublicClientAuthenticationService service
        && service.LastFallbackReason is not FallbackReason.None and not FallbackReason.ExplicitUserFallback;

    /// <summary>
    /// How long the broker was observed before the fallback decision. Diagnostics only; it contains no
    /// identity or token material.
    /// </summary>
    public string BrokerElapsedText => _authentication is PublicClientAuthenticationService service
        && service.LastBrokerElapsed is { } elapsed
            ? $"{elapsed.TotalSeconds:F1}s"
            : "—";

    private static string StateText(CloudSectionState state) => state switch
    {
        CloudSectionState.NotConfigured => "Microsoft 365 is not configured.",
        CloudSectionState.SignedOut => "Not signed in to Microsoft 365.",
        CloudSectionState.SigningIn => "Signing in to Microsoft 365.",
        CloudSectionState.Authenticated => "Signed in. The tenant has not been verified yet.",
        CloudSectionState.TenantVerified => "Tenant verified. The selected user has not been correlated.",
        CloudSectionState.TenantMismatch => "The session belongs to a different tenant. No cloud data was read.",
        CloudSectionState.CorrelationPending => "Reading Microsoft 365 state.",
        CloudSectionState.Verified => "The cloud object is correlated to the selected user.",
        CloudSectionState.Unmatched => "No synchronized cloud object exists for this user.",
        CloudSectionState.Ambiguous => "Several cloud objects match this user. ADEXO will not choose between them.",
        CloudSectionState.Conflict => "The cloud object's evidence contradicts the selected user.",
        CloudSectionState.GraphUnavailable => "Microsoft Graph is unavailable.",
        CloudSectionState.PermissionMissing => "A Microsoft Graph permission is missing.",
        CloudSectionState.Canceled => "The operation was canceled.",
        _ => "Microsoft 365 state is unavailable.",
    };

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _signInCommand.RaiseCanExecuteChanged();
        _signOutCommand.RaiseCanExecuteChanged();
        _refreshCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
