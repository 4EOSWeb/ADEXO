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
/// The production silent acquirer: cached account first, then the operating-system account.
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
        var subject = cached ?? (IAccount?)PublicClientApplication.OperatingSystemAccount;

        if (subject is null)
        {
            return null;
        }

        try
        {
            return await _application
                .AcquireTokenSilent(MicrosoftIdentityConfiguration.Scopes, subject)
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
