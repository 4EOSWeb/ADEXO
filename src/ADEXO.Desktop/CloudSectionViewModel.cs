using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ADEXO.Contracts.Authentication;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Results;
using ADEXO.Core.Correlation;
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
        Func<IntPtr> ownerWindowHandleProvider)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(subjectProvider);
        ArgumentNullException.ThrowIfNull(ownerWindowHandleProvider);

        _signInCommand = new RelayCommand(
            async () => await SignInAsync(ownerWindowHandleProvider()).ConfigureAwait(true),
            () => !_busy);
        _signOutCommand = new RelayCommand(
            async () => await SignOutAsync().ConfigureAwait(true),
            () => !_busy && _account is not null);

        _refreshCommand = new RelayCommand(
            async () => await RefreshAsync(subjectProvider()).ConfigureAwait(true),
            () => !_busy && _account is not null);

        // A configured section starts signed out, not "not configured". Leaving the field default
        // would report the section as unavailable until the operator did something, which reads as a
        // configuration failure that is not there.
        State = CloudSectionState.SignedOut;
        Message = StateText(CloudSectionState.SignedOut);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand SignInCommand => _signInCommand;

    public ICommand SignOutCommand => _signOutCommand;

    public ICommand RefreshCommand => _refreshCommand;

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
        Message = "Signing in to Microsoft 365.";

        // A sign-in that produces no dialog and no result used to leave the section in SigningIn with
        // nothing to inspect. The watchdog reports progress after a threshold; it never cancels a
        // dialog the operator may still be using for MFA.
        using var watchdog = new AuthenticationWatchdog(this);
        watchdog.Start();

        try
        {
            var signIn = await _authentication.SignInAsync(ownerWindowHandle, CancellationToken.None).ConfigureAwait(true);

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

            var (tenant, result) = await _session.EstablishTenantAsync(CancellationToken.None).ConfigureAwait(true);
            _tenant = tenant;
            Apply(result);
        }
        finally
        {
            watchdog.Stop();
            SetBusy(false);
        }
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

            var correlated = await _session.CorrelateAsync(subject, tenant, consistencyGuid: null, CancellationToken.None).ConfigureAwait(true);
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
    }

    /// <summary>
    /// Appends a progress note to the status message. Called by the watchdog; it never changes the
    /// state or cancels anything.
    /// </summary>
    public void ReportProgress(string note)
    {
        Message = string.IsNullOrWhiteSpace(Message) ? note : $"{Message} {note}";
    }

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
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
