using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Results;

namespace ADEXO.Contracts.Authentication;

/// <summary>
/// Interactive Microsoft identity for the signed-in administrator.
/// <para>
/// This abstraction deliberately has no member that returns an access token. The token exists only
/// inside the implementation, where it is attached to authorized requests. Nothing in Core, Contracts,
/// the desktop shell, diagnostics, or the audit trail can obtain one through this interface.
/// </para>
/// </summary>
public interface IMicrosoftAuthenticationService
{
    /// <summary>Current session state, including whether configuration is even present.</summary>
    AuthenticationState State { get; }

    /// <summary>The signed-in administrator, or null. Carries no token.</summary>
    AuthenticatedAccount? CurrentAccount { get; }

    /// <summary>
    /// Attempts a silent acquisition using the cached account, or the operating system account when
    /// no cached account exists. Returns null when interactive sign-in is required.
    /// </summary>
    Task<ProviderResult<AuthenticatedAccount>> TryAcquireSilentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs an explicit, user-triggered interactive sign-in, parented to the supplied window handle so
    /// the broker dialog is owned by ADEXO. This is only ever called from a user action.
    /// </summary>
    Task<ProviderResult<AuthenticatedAccount>> SignInAsync(IntPtr ownerWindowHandle, CancellationToken cancellationToken);

    /// <summary>Removes the cached account and clears the session state.</summary>
    Task SignOutAsync(CancellationToken cancellationToken);
}
