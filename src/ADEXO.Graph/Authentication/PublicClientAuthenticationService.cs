using ADEXO.Contracts.Authentication;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Results;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

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
    private readonly IInteractiveTokenAcquirer _interactiveAcquirer;
    private readonly ISilentTokenAcquirer _silentAcquirer;
    private readonly object _stateLock = new();
    private readonly List<AuthenticationCheckpoint> _checkpoints = [];
    private long _attemptStartedTicks;
    private AuthenticationState _state = AuthenticationState.SignedOut;
    private AuthenticatedAccount? _account;

    /// <summary>
    /// Created by the composition root. The optional seams exist so the threading contract can be
    /// asserted deterministically without a live tenant; production passes none of them.
    /// </summary>
    public PublicClientAuthenticationService(
        MicrosoftIdentityConfiguration configuration,
        IPublicClientApplication? application = null,
        IWindowHandleValidator? windowHandleValidator = null,
        IInteractiveTokenAcquirer? interactiveAcquirer = null,
        ISilentTokenAcquirer? silentAcquirer = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        var client = application ?? BuildApplication(configuration);
        _windowHandleValidator = windowHandleValidator ?? WindowHandleValidator.Instance;
        _interactiveAcquirer = interactiveAcquirer ?? new MsalInteractiveTokenAcquirer(client);
        _silentAcquirer = silentAcquirer ?? new MsalSilentTokenAcquirer(client);
    }

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

        var account = Adopt(silent);
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
            return Complete(Fail(OperationError.Create(FailureCategory.Validation, handleProblem)));
        }

        BeginAttempt();
        SetState(AuthenticationState.Authenticating);

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
                return Complete(Cancel());
            }
            catch (Exception exception) when (exception is not MsalUiRequiredException)
            {
                // Any non-cancellation failure of the silent path is a failure, not a reason to prompt.
                return Complete(Fail(Sanitize(exception)));
            }

            Checkpoint("silent-attempt-returned");

            if (silentResult is not null)
            {
                var adopted = Adopt(silentResult);
                return adopted is null
                    ? Complete(TenantMismatchFailure())
                    : Complete(ProviderResult<AuthenticatedAccount>.Success(adopted));
            }

            // Interaction is required. The interactive call must be initiated on the thread that owns
            // the parent window, because WAM is created and driven from that thread. Awaiting the silent
            // attempt above resumes on a thread-pool thread, so initiating the broker from there would
            // run it off the UI thread and the request would never surface a dialog. That was the
            // defect: the state reached SigningIn and nothing else happened.
            //
            // Awaiting without ConfigureAwait(false) keeps the continuation on the caller's context, so
            // the initiation and the resume both happen on the window's thread.
            Checkpoint("interactive-initiate");
            var interactiveResult = await _interactiveAcquirer
                .AcquireAsync(ownerWindowHandle, cancellationToken)
                .ConfigureAwait(true);

            var account = Adopt(interactiveResult);
            return account is null
                ? Complete(TenantMismatchFailure())
                : Complete(ProviderResult<AuthenticatedAccount>.Success(account));
        }
        catch (MsalUiRequiredException)
        {
            // The broker reported that interaction is required and the interaction itself did not
            // complete. That is a cancel, not a retry trigger.
            return Complete(Cancel());
        }
        catch (OperationCanceledException)
        {
            return Complete(Cancel());
        }
        catch (MsalClientException exception) when (exception.ErrorCode is "authentication_canceled" or "user_canceled")
        {
            return Complete(Cancel());
        }
        catch (MsalClientException exception) when (exception.ErrorCode is "wam_ui_thread")
        {
            // Microsoft documents this code for exactly the defect above. Recording it explicitly keeps
            // a future regression from looking like a generic failure.
            return Complete(Fail(OperationError.Create(
                FailureCategory.Authentication,
                "The interactive request has to start on the thread that owns the parent window (wam_ui_thread).")));
        }
        catch (Exception exception)
        {
            return Complete(Fail(Sanitize(exception)));
        }
    }

    public Task SignOutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetState(AuthenticationState.SignedOut, account: null);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static IPublicClientApplication BuildApplication(MicrosoftIdentityConfiguration configuration) =>
        PublicClientApplicationBuilder
            .Create(configuration.ClientId)
            .WithAuthority(configuration.Authority, validateAuthority: true)
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { MsaPassthrough = false })
            .WithClientName("ADEXO")
            .Build();

    /// <summary>
    /// Applies the token tenant lock (T1) and records the session. Returns null when the token tenant
    /// differs from the configured tenant, so no caller can treat a mismatched session as usable.
    /// </summary>
    private AuthenticatedAccount? Adopt(AuthenticationResult result)
    {
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
            brokerUsed: true);

        SetState(AuthenticationState.Authenticated, account);
        return account;
    }

    private ProviderResult<AuthenticatedAccount> TenantMismatchFailure()
    {
        Checkpoint("tenant-mismatch");
        SetState(AuthenticationState.TenantMismatch, account: null);

        return ProviderResult<AuthenticatedAccount>.Failed(
            OperationError.Create(FailureCategory.Authentication, "The signed-in session belongs to a different tenant than the configured one."));
    }

    private ProviderResult<AuthenticatedAccount> Cancel()
    {
        Checkpoint("canceled");
        SetState(AuthenticationState.Canceled, account: null);
        return ProviderResult<AuthenticatedAccount>.Canceled();
    }

    private ProviderResult<AuthenticatedAccount> Fail(OperationError error)
    {
        Checkpoint($"failed: {error.Category}");
        SetState(AuthenticationState.Failed);
        return ProviderResult<AuthenticatedAccount>.Failed(error);
    }

    /// <summary>Marks the attempt complete so a watchdog can tell a finished attempt from a hung one.</summary>
    private ProviderResult<AuthenticatedAccount> Complete(ProviderResult<AuthenticatedAccount> result)
    {
        Checkpoint("attempt-complete");
        return result;
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
