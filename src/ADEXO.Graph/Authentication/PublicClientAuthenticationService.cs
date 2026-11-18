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
    private readonly IPublicClientApplication _application;
    private readonly MicrosoftIdentityConfiguration _configuration;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AuthenticationState _state = AuthenticationState.SignedOut;
    private AuthenticatedAccount? _account;

    /// <summary>
    /// Created by the composition root. The factory exists so a test can supply its own application
    /// instance without any test double living in a production assembly.
    /// </summary>
    public PublicClientAuthenticationService(
        MicrosoftIdentityConfiguration configuration,
        IPublicClientApplication? application = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _application = application ?? BuildApplication(configuration);
    }

    public AuthenticationState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public AuthenticatedAccount? CurrentAccount
    {
        get
        {
            lock (_gate)
            {
                return _account;
            }
        }
    }

    /// <summary>
    /// The most recent access token, available only to the request pipeline that has to attach it.
    /// This is deliberately not part of <see cref="IMicrosoftAuthenticationService"/>.
    /// </summary>
    internal async Task<ProviderResult<string>> AcquireAccessTokenAsync(CancellationToken cancellationToken)
    {
        var result = await TryAcquireSilentTokenAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && result.Value is not null)
        {
            return ProviderResult<string>.Success(result.Value.AccessToken);
        }

        // A silent failure here means the session needs interaction. That is reported as such rather
        // than throwing, so a Graph request surfaces a clean authorization failure.
        return ProviderResult<string>.Failed(result.Error
            ?? OperationError.Create(FailureCategory.Authentication, "No access token is available."));
    }

    /// <summary>
    /// Supplies the access token to the Graph request adapter. The value is handed straight to the
    /// request and is not retained.
    /// </summary>
    public async Task<string> GetAccessTokenForRequestAsync(CancellationToken cancellationToken)
    {
        var token = await AcquireAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        return token.IsSuccess && token.Value is not null ? token.Value : string.Empty;
    }
    public async Task<ProviderResult<AuthenticatedAccount>> TryAcquireSilentAsync(CancellationToken cancellationToken)
    {
        var silent = await TryAcquireSilentTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!silent.IsSuccess)
        {
            return ProviderResult<AuthenticatedAccount>.Failed(silent.Error!);
        }

        var account = Adopt(silent.Value!);
        return account is null
            ? ProviderResult<AuthenticatedAccount>.Failed(
                OperationError.Create(FailureCategory.Authentication, "The token was issued for a different tenant than the configured one."))
            : ProviderResult<AuthenticatedAccount>.Success(account);
    }

    public async Task<ProviderResult<AuthenticatedAccount>> SignInAsync(IntPtr ownerWindowHandle, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _state = AuthenticationState.Authenticating;
        }

        try
        {
            // A silent acquisition is attempted first so an already-signed-in account does not show a
            // dialog. Only MsalUiRequiredException justifies an interactive prompt.
            var silent = await TryAcquireSilentTokenAsync(cancellationToken).ConfigureAwait(false);
            if (silent.IsSuccess)
            {
                var adopted = Adopt(silent.Value!);
                return adopted is null
                    ? TenantMismatchFailure()
                    : ProviderResult<AuthenticatedAccount>.Success(adopted);
            }

            if (silent.Error?.Category != FailureCategory.Authentication)
            {
                return Fail(silent.Error!);
            }

            var interactiveResult = await _application
                .AcquireTokenInteractive(MicrosoftIdentityConfiguration.Scopes)
                .WithParentActivityOrWindow(ownerWindowHandle)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            var account = Adopt(interactiveResult);
            return account is null
                ? TenantMismatchFailure()
                : ProviderResult<AuthenticatedAccount>.Success(account);
        }
        catch (MsalUiRequiredException)
        {
            // The broker reported that interaction is required and the interaction itself did not
            // complete. That is a cancel, not a retry trigger.
            return Cancel("The sign-in did not complete. No token was obtained.");
        }
        catch (OperationCanceledException)
        {
            return Cancel("The sign-in was canceled.");
        }
        catch (MsalClientException exception) when (exception.ErrorCode is "authentication_canceled" or "user_canceled")
        {
            return Cancel("The sign-in was canceled.");
        }
        catch (Exception exception)
        {
            return Fail(Sanitize(exception));
        }
    }

    public Task SignOutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _account = null;
            _state = AuthenticationState.SignedOut;
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private static IPublicClientApplication BuildApplication(MicrosoftIdentityConfiguration configuration) =>
        PublicClientApplicationBuilder
            .Create(configuration.ClientId)
            .WithAuthority(configuration.Authority, validateAuthority: true)
            .WithRedirectUri(configuration.RedirectUri)
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { MsaPassthrough = false })
            .WithClientName("ADEXO")
            .Build();

    private async Task<ProviderResult<AuthenticationResult>> TryAcquireSilentTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var accounts = await _application.GetAccountsAsync().ConfigureAwait(false);
            var cached = accounts.FirstOrDefault();
            var subject = cached ?? (IAccount?)PublicClientApplication.OperatingSystemAccount;

            if (subject is null)
            {
                return ProviderResult<AuthenticationResult>.Failed(
                    OperationError.Create(FailureCategory.Authentication, "No account is available for a silent sign-in."));
            }

            var result = await _application
                .AcquireTokenSilent(MicrosoftIdentityConfiguration.Scopes, subject)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return ProviderResult<AuthenticationResult>.Success(result);
        }
        catch (MsalUiRequiredException)
        {
            return ProviderResult<AuthenticationResult>.Failed(
                OperationError.Create(FailureCategory.Authentication, "Interaction is required to sign in."));
        }
        catch (OperationCanceledException)
        {
            return ProviderResult<AuthenticationResult>.Canceled();
        }
        catch (Exception exception)
        {
            return ProviderResult<AuthenticationResult>.Failed(Sanitize(exception));
        }
    }

    /// <summary>
    /// Applies the token tenant lock (T1) and records the session. Returns null when the token tenant
    /// differs from the configured tenant, so no caller can treat a mismatched session as usable.
    /// </summary>
    private AuthenticatedAccount? Adopt(AuthenticationResult result)
    {
        if (!Guid.TryParse(result.TenantId, out var tokenTenant) || tokenTenant != _configuration.TenantId)
        {
            lock (_gate)
            {
                _account = null;
                _state = AuthenticationState.TenantMismatch;
            }

            return null;
        }

        var account = AuthenticatedAccount.Create(
            result.UniqueId,
            tokenTenant,
            result.Account?.Username,
            brokerUsed: true);

        lock (_gate)
        {
            _account = account;
            _state = AuthenticationState.Authenticated;
        }

        return account;
    }

    private ProviderResult<AuthenticatedAccount> TenantMismatchFailure()
    {
        lock (_gate)
        {
            _account = null;
            _state = AuthenticationState.TenantMismatch;
        }

        return ProviderResult<AuthenticatedAccount>.Failed(
            OperationError.Create(FailureCategory.Authentication, "The signed-in session belongs to a different tenant than the configured one."));
    }

    private ProviderResult<AuthenticatedAccount> Cancel(string reason)
    {
        lock (_gate)
        {
            _account = null;
            _state = AuthenticationState.Canceled;
        }

        return ProviderResult<AuthenticatedAccount>.Canceled();
    }

    private ProviderResult<AuthenticatedAccount> Fail(OperationError error)
    {
        lock (_gate)
        {
            _state = AuthenticationState.Failed;
        }

        return ProviderResult<AuthenticatedAccount>.Failed(error);
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
