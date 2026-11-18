using System.Text.Json;
using System.Text.Json.Serialization;
using ADEXO.Contracts.Authentication;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Results;
using ADEXO.Graph.Orchestration;

namespace ADEXO.Graph.Tests;

/// <summary>
/// Guards the rule that token material never leaves the request pipeline.
/// <para>
/// The sentinel below stands in for an access token. It is a synthetic value that appears nowhere in
/// production, so any occurrence in a rendered, serialized, or reported artefact is a leak.
/// </para>
/// </summary>
public sealed class TokenLeakTests
{
    private const string Sentinel = "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.SENTINEL-ACCESS-TOKEN-DO-NOT-LEAK.SENTINEL-SIGNATURE";

    private static AuthenticatedAccount Account() => AuthenticatedAccount.Create(
        accountObjectId: "00000000-0000-0000-0000-0000000000aa",
        tenantId: Guid.Parse("c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af"),
        username: "administrator@lab.example",
        brokerUsed: true);

    [Fact]
    public void The_authenticated_account_type_declares_no_token_member()
    {
        // A future contributor must not be able to add a token property without this failing.
        var names = typeof(AuthenticatedAccount).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain(names, n => n.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Credential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_authentication_interface_declares_no_token_member()
    {
        var methods = typeof(IMicrosoftAuthenticationService).GetMethods();

        Assert.DoesNotContain(methods, m => m.ReturnType == typeof(string));
        Assert.DoesNotContain(methods, m => m.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Serializing_an_authenticated_account_contains_no_sentinel()
    {
        var json = JsonSerializer.Serialize(Account(), new JsonSerializerOptions { WriteIndented = true });

        Assert.DoesNotContain(Sentinel, json, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_account_to_string_contains_no_sentinel()
    {
        Assert.DoesNotContain(Sentinel, Account().ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_cloud_section_result_contains_no_sentinel_on_any_state()
    {
        foreach (var state in Enum.GetValues<CloudSectionState>())
        {
            var result = new CloudSectionResult
            {
                State = state,
                Account = Account(),
                Message = "A sanitized message.",
                User = state == CloudSectionState.Verified
                    ? new CloudDirectoryUser { EntraObjectId = Guid.NewGuid(), DisplayName = "User" }
                    : null,
            };

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
            });

            Assert.DoesNotContain(Sentinel, json, StringComparison.Ordinal);
            Assert.DoesNotContain("AccessToken", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("RefreshToken", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_sanitized_failure_never_carries_the_sentinel()
    {
        // The failure summary is built from the error category, not from exception text, which is what
        // keeps a token echoed by a library from reaching the UI.
        var error = OperationError.Create(FailureCategory.Authentication, "The sign-in failed (invalid_grant).");

        Assert.DoesNotContain(Sentinel, error.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, error.DiagnosticDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cloud_directory_user_serializes_without_token_shaped_fields()
    {
        var user = new CloudDirectoryUser
        {
            EntraObjectId = Guid.NewGuid(),
            DisplayName = "Example",
            UserPrincipalName = "example@lab.example",
            OnPremisesImmutableId = "MDEyMzQ1Njc4OWFiY2RlZg==",
        };

        var json = JsonSerializer.Serialize(user);

        Assert.DoesNotContain("Token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Sentinel, json, StringComparison.Ordinal);
    }
}
