using ADEXO.Contracts.Authentication;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Results;
using Microsoft.Identity.Client;

namespace ADEXO.Graph.Authentication;

/// <summary>
/// The interactive Microsoft identity implementation: a public client using the authorization code
/// flow, brokered by the Windows account manager where the platform supports it.
/// <para>
/// The access token never leaves this class through a public member. Downstream code receives an
/// <see cref="AuthenticatedAccount"/>, which carries display and tenant-lock data only. Nothing here
/// uses a client secret, a certificate, a device code, username/password, or an application-only flow.
/// </para>
/// </summary>
public sealed class PublicClientAuthenticationService : IMicrosoftAuthenticationService, IAsyncDisposable
{
    private readonly MicrosoftIdentityConfiguration _configuration;
    private readonly IWindowHandleValidator _windowHandleValidator;
    private readonly IInteractiveTokenAcquirer _wamAcquirer;
    private readonly IInteractiveTokenAcquirer _browserAcquirer;
    private readonly ISilentTokenAcquirer _silentAcquirer;
    private readonly IBrokerSurfaceDetector? _surfaceDetector;
    private readonly TimeSpan _brokerSurfacingWindow;
    private readonly IAccountCacheClearer _cacheClearer;
    private readonly object _stateLock = new();
    private readonly List<AuthenticationCheckpoint> _checkpoints = [];
    private long _attemptStartedTicks;
    private int _currentAttemptId;
    private int _retiredAttemptId;
    private AuthenticationState _state = AuthenticationState.SignedOut;
    private AuthenticatedAccount? _account;

    /// <summary>
    /// Created by the composition root. The optional seams exist so the threading contract and the
    /// fallback behaviour can be asserted deterministically without a live tenant; production passes
    /// none of them.
    /// </summary>
    public PublicClientAuthenticationService(
        MicrosoftIdentityConfiguration configuration,
        IPublicClientApplication? application = null,
        IWindowHandleValidator? windowHandleValidator = null,
        IInteractiveTokenAcquirer? interactiveAcquirer = null,
        ISilentTokenAcquirer? silentAcquirer = null,
        IInteractiveTokenAcquirer? browserAcquirer = null,
        AuthenticationMode mode = AuthenticationMode.Auto,
        IBrokerSurfaceDetector? surfaceDetector = null,
        TimeSpan? brokerSurfacingWindow = null,
        IAccountCacheClearer? cacheClearer = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        var wamClient = application ?? IdentityClientFactory.Create(configuration, IdentityClientKind.WindowsBroker);
        var browserClient = IdentityClientFactory.Create(configuration, IdentityClientKind.SystemBrowser);

        _windowHandleValidator = windowHandleValidator ?? WindowHandleValidator.Instance;
        _wamAcquirer = interactiveAcquirer ?? new MsalInteractiveTokenAcquirer(wamClient);
        _browserAcquirer = browserAcquirer ?? new MsalInteractiveTokenAcquirer(browserClient);
        _silentAcquirer = silentAcquirer ?? new MsalSilentTokenAcquirer(wamClient);
        _cacheClearer = cacheClearer ?? new MsalAccountCacheClearer(wamClient, browserClient);

        // Auto is the only mode that needs to tell "the broker showed its UI" from "the broker showed
        // nothing", so the detector is attached only when a fallback decision can occur. WamOnly and
        // SystemBrowserOnly never make that decision and therefore never enumerate windows.
        _surfaceDetector = surfaceDetector
            ?? (mode == AuthenticationMode.Auto ? BrokerWindowDetector.Instance : null);

        _brokerSurfacingWindow = brokerSurfacingWindow ?? MicrosoftIdentityConfiguration.BrokerSurfacingWindow;

        Mode = mode;
    }

    /// <summary>The configured authentication mode. Never silently overridden.</summary>
    public AuthenticationMode Mode { get; }

    /// <summary>The mechanism that produced the most recent completed attempt.</summary>
    public AuthenticationMethod LastMethod { get; private set; } = AuthenticationMethod.None;

    /// <summary>Why the most recent attempt fell back, or None.</summary>
    public FallbackReason LastFallbackReason { get; private set; } = FallbackReason.None;

    /// <summary>How long the broker was observed before a fallback decision, when one occurred.</summary>
    public TimeSpan? LastBrokerElapsed { get; private set; }

    /// <summary>
    /// Raised immediately before the system browser is launched in Auto mode. The shell uses it to tell
    /// the administrator why a browser window is about to appear. It carries no token and no identity.
    /// </summary>
    public event EventHandler<FallbackReason>? BrowserFallbackStarting;

    /// <summary>
    /// Raised whenever the authentication state changes. Handlers run outside the state lock, so a
    /// handler may freely read <see cref="State"/> or <see cref="CurrentAccount"/>.
    /// </summary>
    public event EventHandler<AuthenticationState>? StateChanged;

    public AuthenticationState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public AuthenticatedAccount? CurrentAccount
    {
        get
        {
            lock (_stateLock)
            {
                return _account;
            }
        }
    }

    /// <summary>
    /// The checkpoints reached during the most recent sign-in attempt. Thread metadata and elapsed
    /// time only; this exists so a hang produces evidence instead of silence.
    /// </summary>
    public IReadOnlyList<AuthenticationCheckpoint> LastAttemptCheckpoints
    {
        get
        {
            lock (_stateLock)
            {
                return [.. _checkpoints];
            }
        }
    }

    /// <summary>
    /// Supplies the access token to the Graph request adapter. The value is handed straight to the
    /// request and is not retained.
    /// </summary>
    public async Task<string> GetAccessTokenForRequestAsync(CancellationToken cancellationToken)
    {
        var result = await _silentAcquirer.TryAcquireAsync(cancellationToken).ConfigureAwait(false);
        return result?.AccessToken ?? string.Empty;
    }

    public async Task<ProviderResult<AuthenticatedAccount>> TryAcquireSilentAsync(CancellationToken cancellationToken)
    {
        AuthenticationResult? silent;
        try
        {
            silent = await _silentAcquirer.TryAcquireAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ProviderResult<AuthenticatedAccount>.Canceled();
        }
        catch (Exception exception)
        {
            return ProviderResult<AuthenticatedAccount>.Failed(Sanitize(exception));
        }

        if (silent is null)
        {
            return ProviderResult<AuthenticatedAccount>.Failed(
                OperationError.Create(FailureCategory.Authentication, "Interaction is required to sign in."));
        }

        var account = Adopt(Interlocked.Increment(ref _currentAttemptId), silent, AuthenticationMethod.None);
        return account is null
            ? ProviderResult<AuthenticatedAccount>.Failed(
                OperationError.Create(FailureCategory.Authentication, "The token was issued for a different tenant than the configured one."))
            : ProviderResult<AuthenticatedAccount>.Success(account);
    }

    public async Task<ProviderResult<AuthenticatedAccount>> SignInAsync(IntPtr ownerWindowHandle, CancellationToken cancellationToken)
    {
        // A zero, stale, or foreign handle must be rejected here rather than handed to the broker,
        // where it can fail in ways that never surface as an exception.
        var handleProblem = _windowHandleValidator.Validate(ownerWindowHandle);
        if (handleProblem is not null)
        {
            return Complete(Fail(0, OperationError.Create(FailureCategory.Validation, handleProblem)));
        }

        BeginAttempt();
        SetState(AuthenticationState.Authenticating);

        // Every interactive attempt carries an attempt id. A result from an older attempt is discarded,
        // so a late broker completion can never overwrite a newer browser result.
        var attemptId = NextAttemptId();

        try
        {
            // The silent attempt happens first. Its continuations deliberately do not capture a context:
            // it is pure I/O and does not touch the UI. Returns null when interaction is required.
            AuthenticationResult? silentResult;
            try
            {
                silentResult = await _silentAcquirer.TryAcquireAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Complete(Cancel(attemptId));
            }
            catch (Exception exception) when (exception is not MsalUiRequiredException)
            {
                // Any non-cancellation failure of the silent path is a failure, not a reason to prompt.
                return Complete(Fail(attemptId, Sanitize(exception)));
            }

            Checkpoint("silent-attempt-returned");

            if (silentResult is not null)
            {
                var adopted = Adopt(attemptId, silentResult, AuthenticationMethod.None);
                return adopted is null
                    ? Complete(TenantMismatchFailure(attemptId))
                    : Complete(ProviderResult<AuthenticatedAccount>.Success(adopted));
            }

            // Interaction is required. Which mechanism runs first depends on the configured mode.
            return Mode switch
            {
                AuthenticationMode.WamOnly => await RunWamOnlyAsync(ownerWindowHandle, attemptId, cancellationToken).ConfigureAwait(true),
                AuthenticationMode.SystemBrowserOnly => await RunBrowserAsync(ownerWindowHandle, attemptId, FallbackReason.ExplicitUserFallback, null, cancellationToken).ConfigureAwait(true),
                _ => await RunAutoAsync(ownerWindowHandle, attemptId, cancellationToken).ConfigureAwait(true),
            };
        }
        catch (MsalClientException exception) when (exception.ErrorCode is "authentication_canceled" or "user_canceled")
        {
            return Complete(Cancel(attemptId));
        }
        catch (OperationCanceledException)
        {
            return Complete(Cancel(attemptId));
        }
        catch (MsalClientException exception) when (exception.ErrorCode is "wam_ui_thread")
        {
            // Microsoft documents this code for starting the interactive request off the owning thread.
            // Recording it explicitly keeps a future regression from looking like a generic failure.
            return Complete(Fail(attemptId, OperationError.Create(
                FailureCategory.Authentication,
                "The interactive request has to start on the thread that owns the parent window (wam_ui_thread).")));
        }
        catch (Exception exception)
        {
            return Complete(Fail(attemptId, Sanitize(exception)));
        }
    }

    /// <summary>
    /// WAM only. The system browser is never launched, even on failure, because an administrator
    /// configured this mode to prevent it. A failure is reported honestly rather than overridden.
    /// </summary>
    private async Task<ProviderResult<AuthenticatedAccount>> RunWamOnlyAsync(
        IntPtr ownerWindowHandle,
        int attemptId,
        CancellationToken cancellationToken)
    {
        Checkpoint("interactive-initiate-wam");
        LastFallbackReason = FallbackReason.None;
        LastMethod = AuthenticationMethod.WindowsBroker;

        AuthenticationResult result;
        try
        {
            result = await _wamAcquirer.AcquireAsync(ownerWindowHandle, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return Complete(Cancel(attemptId));
        }
        catch (MsalClientException exception) when (exception.ErrorCode is "authentication_canceled" or "user_canceled")
        {
            return Complete(Cancel(attemptId));
        }

        var account = Adopt(attemptId, result, AuthenticationMethod.WindowsBroker);
        return account is null
            ? Complete(TenantMismatchFailure(attemptId))
            : Complete(ProviderResult<AuthenticatedAccount>.Success(account));
    }

    /// <summary>
    /// Browser only. The broker is never attempted, which is what makes this mode useful for isolating
    /// the browser path.
    /// </summary>
    private async Task<ProviderResult<AuthenticatedAccount>> RunBrowserAsync(
        IntPtr ownerWindowHandle,
        int attemptId,
        FallbackReason fallbackReason,
        TimeSpan? brokerElapsed,
        CancellationToken cancellationToken)
    {
        Checkpoint("interactive-initiate-browser");
        LastFallbackReason = fallbackReason;
        LastMethod = AuthenticationMethod.SystemBrowser;
        LastBrokerElapsed = brokerElapsed;

        if (fallbackReason is not FallbackReason.None and not FallbackReason.ExplicitUserFallback)
        {
            // Announce the fallback before the browser opens, so the message is on screen while the
            // administrator is looking at a browser window and wondering why it appeared.
            BrowserFallbackStarting?.Invoke(this, fallbackReason);
        }

        AuthenticationResult result;
        try
        {
            result = await _browserAcquirer.AcquireAsync(ownerWindowHandle, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return Complete(Cancel(attemptId));
        }
        catch (MsalClientException exception) when (exception.ErrorCode is "authentication_canceled" or "user_canceled")
        {
            return Complete(Cancel(attemptId));
        }

        var account = Adopt(attemptId, result, AuthenticationMethod.SystemBrowser);
        return account is null
            ? Complete(TenantMismatchFailure(attemptId))
            : Complete(ProviderResult<AuthenticatedAccount>.Success(account));
    }

    /// <summary>
    /// The default mode: prefer the broker, and fall back to the browser only when usable broker
    /// interaction has not begun within the bounded window.
    /// <para>
    /// The two flows never run concurrently as active sessions. The broker attempt is started, observed,
    /// and if it does not surface, its attempt id is retired so its eventual result is discarded rather
    /// than applied.
    /// </para>
    /// </summary>
    private async Task<ProviderResult<AuthenticatedAccount>> RunAutoAsync(
        IntPtr ownerWindowHandle,
        int attemptId,
        CancellationToken cancellationToken)
    {
        Checkpoint("interactive-initiate-wam");
        var probe = new BrokerSurfacingProbe(_brokerSurfacingWindow, _surfaceDetector);
        probe.Start(ownerWindowHandle);

        // The broker call must be initiated on the thread that owns the parent window, so it is started
        // here rather than inside a task that would resume on a thread-pool thread.
        var brokerTask = _wamAcquirer.AcquireAsync(ownerWindowHandle, cancellationToken);

        // Observe the broker for the surfacing window. Once UI is reported, the window stops applying.
        AuthenticationResult? observed;
        try
        {
            observed = await probe
                .AwaitOrExpireAsync(brokerTask, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            probe.Stop();
            return Complete(Cancel(attemptId));
        }
        catch (Exception exception) when (BrokerFailureClassifier.IsUserCancellation(exception))
        {
            // The operator dismissed the broker dialog. That is an answer, not a broker defect, so it is
            // reported as a cancellation rather than silently retried in the browser.
            probe.Stop();
            return Complete(Cancel(attemptId));
        }
        catch (Exception exception) when (BrokerFailureClassifier.IsBrokerFailure(exception))
        {
            // A broker that fails to activate is precisely the case the fallback exists for. Failing the
            // whole sign-in here would leave Auto no better than WamOnly.
            probe.Stop();
            RetireAttempt(attemptId);
            return await RunBrowserAsync(
                ownerWindowHandle,
                NextAttemptId(),
                FallbackReason.BrokerActivationFailure,
                probe.Elapsed,
                cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            // Any other failure is the operator's or the tenant's own answer (a policy refusal, a consent
            // requirement, a licence problem). Retrying it in a different browser would not resolve it
            // and would obscure the real reason, so it is reported honestly.
            probe.Stop();
            return Complete(Fail(attemptId, Sanitize(exception)));
        }

        probe.Stop();

        if (observed is not null && !IsSuperseded(attemptId))
        {
            // The broker produced a token without the browser path ever being considered.
            LastFallbackReason = FallbackReason.None;
            LastMethod = AuthenticationMethod.WindowsBroker;
            LastBrokerElapsed = probe.Elapsed;
            var account = Adopt(attemptId, observed, AuthenticationMethod.WindowsBroker);
            return account is null
                ? Complete(TenantMismatchFailure(attemptId))
                : Complete(ProviderResult<AuthenticatedAccount>.Success(account));
        }

        // Reaching here means the observation window closed with no usable broker UI and no token. The
        // classification is what the diagnostics record, so it is decided before the attempt is retired.
        var reason = probe.Classify(activationFailed: false);
        var elapsed = probe.Elapsed;

        // Retire this attempt first, so a broker that surfaces late cannot change application state
        // after the browser session has been adopted.
        RetireAttempt(attemptId);
        ObserveDiscardedBroker(brokerTask);

        return await RunBrowserAsync(
            ownerWindowHandle,
            NextAttemptId(),
            reason,
            elapsed,
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Ensures a retired broker task never becomes an unobserved exception when it eventually finishes.
    /// </summary>
    private static void ObserveDiscardedBroker(Task<AuthenticationResult> brokerTask) =>
        _ = brokerTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The cached account must actually be removed. Clearing only the in-memory session would let the
        // next silent acquisition succeed, so "Sign out" would appear to work while the administrator
        // stayed signed in. A cache failure is reported rather than hidden, but the session is cleared
        // either way: an administrator asking to sign out must not stay visually signed in.
        try
        {
            await _cacheClearer.ClearAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            SetState(AuthenticationState.SignedOut, account: null);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Applies the token tenant lock (T1) and records the session. Returns null when the token tenant
    /// differs from the configured tenant, so no caller can treat a mismatched session as usable.
    /// </summary>
    private AuthenticatedAccount? Adopt(int attemptId, AuthenticationResult result, AuthenticationMethod method)
    {
        // A result from a retired attempt must never change state: a late broker completion could
        // otherwise replace a newer browser result.
        if (IsSuperseded(attemptId))
        {
            Checkpoint("discarded-superseded-attempt");
            return null;
        }

        Checkpoint("token-received");

        if (!Guid.TryParse(result.TenantId, out var tokenTenant) || tokenTenant != _configuration.TenantId)
        {
            SetState(AuthenticationState.TenantMismatch, account: null);
            return null;
        }

        var account = AuthenticatedAccount.Create(
            result.UniqueId,
            tokenTenant,
            result.Account?.Username,
            brokerUsed: method == AuthenticationMethod.WindowsBroker);

        if (method != AuthenticationMethod.None)
        {
            LastMethod = method;
        }

        SetState(AuthenticationState.Authenticated, account);
        return account;
    }

    private ProviderResult<AuthenticatedAccount> TenantMismatchFailure(int attemptId)
    {
        Checkpoint("tenant-mismatch");
        if (!IsSuperseded(attemptId))
        {
            SetState(AuthenticationState.TenantMismatch, account: null);
        }

        return ProviderResult<AuthenticatedAccount>.Failed(
            OperationError.Create(FailureCategory.Authentication, "The signed-in session belongs to a different tenant than the configured one."));
    }

    private ProviderResult<AuthenticatedAccount> Cancel(int attemptId)
    {
        Checkpoint("canceled");
        if (!IsSuperseded(attemptId))
        {
            SetState(AuthenticationState.Canceled, account: null);
        }

        return ProviderResult<AuthenticatedAccount>.Canceled();
    }

    private ProviderResult<AuthenticatedAccount> Fail(int attemptId, OperationError error)
    {
        Checkpoint($"failed: {error.Category}");
        if (!IsSuperseded(attemptId))
        {
            SetState(AuthenticationState.Failed);
        }

        return ProviderResult<AuthenticatedAccount>.Failed(error);
    }

    /// <summary>Marks the attempt complete so a watchdog can tell a finished attempt from a hung one.</summary>
    private ProviderResult<AuthenticatedAccount> Complete(ProviderResult<AuthenticatedAccount> result)
    {
        Checkpoint("attempt-complete");
        return result;
    }

    private int NextAttemptId()
    {
        var id = Interlocked.Increment(ref _currentAttemptId);
        lock (_stateLock)
        {
            _retiredAttemptId = 0;
        }

        return id;
    }

    /// <summary>Retires an attempt so its eventual result is ignored.</summary>
    private void RetireAttempt(int attemptId)
    {
        lock (_stateLock)
        {
            _retiredAttemptId = attemptId;
        }

        Checkpoint("attempt-retired");
    }

    private bool IsSuperseded(int attemptId)
    {
        lock (_stateLock)
        {
            return attemptId < _currentAttemptId || attemptId == _retiredAttemptId;
        }
    }

    /// <summary>
    /// Commits a state change under a brief lock, then raises the event after the lock is released.
    /// Holding a Monitor across an await, or raising a handler that reads state while the same lock is
    /// held, is how a UI handler ends up waiting on a lock its own thread already owns.
    /// </summary>
    private void SetState(AuthenticationState state, AuthenticatedAccount? account = null)
    {
        lock (_stateLock)
        {
            _state = state;
            _account = account;
        }

        StateChanged?.Invoke(this, state);
    }

    private void BeginAttempt()
    {
        lock (_stateLock)
        {
            _checkpoints.Clear();
            _attemptStartedTicks = Environment.TickCount64;
        }

        Checkpoint("attempt-begin");
    }

    private void Checkpoint(string stage)
    {
        var thread = Thread.CurrentThread;
        long elapsed;
        lock (_stateLock)
        {
            elapsed = _attemptStartedTicks == 0 ? 0 : Environment.TickCount64 - _attemptStartedTicks;
            _checkpoints.Add(new AuthenticationCheckpoint(stage, Environment.CurrentManagedThreadId, thread.GetApartmentState().ToString(), elapsed));
        }
    }

    /// <summary>
    /// Reduces a library exception to its type and error code. Exception messages from the identity
    /// library can echo request detail, so the message text is not forwarded.
    /// </summary>
    private static OperationError Sanitize(Exception exception)
    {
        var code = exception is MsalException msal ? msal.ErrorCode : exception.GetType().Name;
        return OperationError.Create(FailureCategory.Authentication, $"The sign-in failed ({code}).");
    }
}
