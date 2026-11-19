using Microsoft.Identity.Client;

namespace ADEXO.Graph.Tests;

/// <summary>
/// Builds identity-library results for tests without contacting a tenant.
/// <para>
/// The synthetic access token is a fixed non-secret marker so the leak tests can assert that no value
/// shaped like a token reaches a rendered or serialized artefact.
/// </para>
/// </summary>
internal static class AuthenticationResults
{
    public const string SyntheticAccessToken =
        "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.SENTINEL-ACCESS-TOKEN-DO-NOT-LEAK.SENTINEL-SIGNATURE";

    public static AuthenticationResult For(
        Guid tenantId,
        string uniqueId = "00000000-0000-0000-0000-0000000000aa",
        string username = "administrator@lab.example")
    {
        var now = DateTimeOffset.UtcNow;

        return new AuthenticationResult(
            SyntheticAccessToken,
            isExtendedLifeTimeToken: false,
            uniqueId,
            expiresOn: now.AddHours(1),
            extendedExpiresOn: now.AddHours(2),
            tenantId: tenantId.ToString("D"),
            account: new TestAccount(username, tenantId),
            idToken: "synthetic-id-token",
            scopes: ["User.Read"],
            correlationId: Guid.Empty,
            authenticationResultMetadata: new AuthenticationResultMetadata(TokenSource.IdentityProvider),
            tokenType: "Bearer");
    }

    private sealed class TestAccount(string username, Guid tenantId) : IAccount
    {
        public string Username { get; } = username;

        public string Environment { get; } = "login.microsoftonline.com";

        public AccountId HomeAccountId { get; } = new($"{tenantId:D}.{username}.{username}");
    }
}
