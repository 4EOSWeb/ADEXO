using ADEXO.Contracts.Authentication;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Results;

namespace ADEXO.Desktop.Tests;

/// <summary>
/// A stub authentication service for view-model tests. It lives in the test assembly only; no test
/// double exists in any production assembly.
/// </summary>
internal sealed class StubAuthenticationService : IMicrosoftAuthenticationService
{
    public AuthenticationState State { get; set; } = AuthenticationState.SignedOut;

    public AuthenticatedAccount? CurrentAccount { get; set; }

    public Task<ProviderResult<AuthenticatedAccount>> TryAcquireSilentAsync(CancellationToken cancellationToken) =>
        Task.FromResult(CurrentAccount is null
            ? ProviderResult<AuthenticatedAccount>.Failed(OperationError.Create(FailureCategory.Authentication, "No account."))
            : ProviderResult<AuthenticatedAccount>.Success(CurrentAccount));

    public Task<ProviderResult<AuthenticatedAccount>> SignInAsync(IntPtr ownerWindowHandle, CancellationToken cancellationToken) =>
        Task.FromResult(CurrentAccount is null
            ? ProviderResult<AuthenticatedAccount>.Canceled()
            : ProviderResult<AuthenticatedAccount>.Success(CurrentAccount));

    public Task SignOutAsync(CancellationToken cancellationToken)
    {
        CurrentAccount = null;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A Graph read client that fails loudly if a test reaches the network. These view-model tests never
/// authenticate, so any read indicates a test that is doing more than it claims.
/// </summary>
internal sealed class NeverCalledReadClient : ADEXO.Graph.Reads.IGraphReadClient
{
    public Task<ProviderResult<Guid>> ReadOrganizationIdAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No Graph call is expected in this test.");

    public Task<ProviderResult<IReadOnlyList<CloudDirectoryUser>>> ReadUsersByImmutableIdAsync(
        string escapedImmutableId,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No Graph call is expected in this test.");

    public Task<ProviderResult<IReadOnlyList<CloudSubscribedSku>>> ReadSubscribedSkusAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No Graph call is expected in this test.");
}
