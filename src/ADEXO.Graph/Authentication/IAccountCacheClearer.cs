using Microsoft.Identity.Client;

namespace ADEXO.Graph.Authentication;

/// <summary>
/// Clears the accounts this application has cached, behind a seam.
/// <para>
/// Sign-out must actually remove the cached account. Clearing only the in-memory session state would
/// leave the next silent acquisition able to succeed, so "Sign out" would appear to work while the
/// administrator remained signed in. Both clients are cleared: the broker client and the browser
/// client keep separate caches, and a stale account in either one is the same defect.
/// </para>
/// </summary>
public interface IAccountCacheClearer
{
    Task ClearAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The production clearer. It removes every cached account from each supplied client using the
/// library's own removal API; nothing here touches the token cache directly.
/// </summary>
public sealed class MsalAccountCacheClearer : IAccountCacheClearer
{
    private readonly IReadOnlyList<IPublicClientApplication> _applications;

    public MsalAccountCacheClearer(params IPublicClientApplication[] applications) =>
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        foreach (var application in _applications)
        {
            var accounts = await application.GetAccountsAsync().ConfigureAwait(false);

            foreach (var account in accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await application.RemoveAsync(account).ConfigureAwait(false);
            }
        }
    }
}
