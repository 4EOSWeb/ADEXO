using Microsoft.Identity.Client;

namespace ADEXO.Graph.Authentication;

/// <summary>
/// The silent acquisition, behind a seam.
/// <para>
/// Its return contract is deliberately narrow: either a token result, or the information that
/// interaction is required. The distinction matters because interaction is never triggered by an
/// arbitrary failure, only by that specific condition.
/// </para>
/// </summary>
public interface ISilentTokenAcquirer
{
    /// <summary>
    /// Attempts a silent acquisition. Returns null when interaction is required, and throws for any
    /// other failure so the caller can map it to a failure rather than a prompt.
    /// </summary>
    Task<AuthenticationResult?> TryAcquireAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The production silent acquirer: a cached account only.
/// <para>
/// The operating-system account is deliberately not used. It only resolves on a device that holds a
/// Microsoft Entra primary refresh token, and on a device that is merely domain joined it produces an
/// account object that can never yield a token. Asking for it there does not fail cleanly; it stalls,
/// which is what the lab observed. Attempting silent acquisition is therefore limited to an account
/// this application has already cached, and everything else goes straight to the interactive path.
/// </para>
/// </summary>
public sealed class MsalSilentTokenAcquirer : ISilentTokenAcquirer
{
    private readonly IPublicClientApplication _application;

    public MsalSilentTokenAcquirer(IPublicClientApplication application) =>
        _application = application ?? throw new ArgumentNullException(nameof(application));

    public async Task<AuthenticationResult?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var accounts = await _application.GetAccountsAsync().ConfigureAwait(false);
        var cached = accounts.FirstOrDefault();

        // No cached account means interaction is required. This is an ordinary first-run state, not an
        // error, and it must not be reported as one.
        if (cached is null)
        {
            return null;
        }

        try
        {
            return await _application
                .AcquireTokenSilent(MicrosoftIdentityConfiguration.Scopes, cached)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MsalUiRequiredException)
        {
            // The only condition that justifies an interactive prompt.
            return null;
        }
    }
}
