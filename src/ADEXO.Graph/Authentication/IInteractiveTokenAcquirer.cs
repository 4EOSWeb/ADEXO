using Microsoft.Identity.Client;

namespace ADEXO.Graph.Authentication;

/// <summary>
/// The interactive broker call, behind a seam.
/// <para>
/// It exists so the threading contract can be asserted deterministically: the live lab found that the
/// broker was initiated from a thread-pool continuation rather than the thread that owns the parent
/// window, and only a substitutable seam lets a test prove which thread starts the call.
/// </para>
/// </summary>
public interface IInteractiveTokenAcquirer
{
    /// <summary>Runs the interactive acquisition, parenting the broker to the supplied window.</summary>
    Task<AuthenticationResult> AcquireAsync(IntPtr ownerWindowHandle, CancellationToken cancellationToken);
}

/// <summary>
/// The production acquirer. A caller invokes it on the thread that owns the parent window.
/// </summary>
public sealed class MsalInteractiveTokenAcquirer : IInteractiveTokenAcquirer
{
    private readonly IPublicClientApplication _application;

    public MsalInteractiveTokenAcquirer(IPublicClientApplication application) =>
        _application = application ?? throw new ArgumentNullException(nameof(application));

    public Task<AuthenticationResult> AcquireAsync(IntPtr ownerWindowHandle, CancellationToken cancellationToken) =>
        _application
            .AcquireTokenInteractive(MicrosoftIdentityConfiguration.Scopes)
            .WithParentActivityOrWindow(ownerWindowHandle)
            .ExecuteAsync(cancellationToken);
}
