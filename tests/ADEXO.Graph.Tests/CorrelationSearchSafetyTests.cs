using System.Reflection;

namespace ADEXO.Graph.Tests;

/// <summary>
/// Proves identity correlation cannot fall back to a mutable name.
/// <para>
/// A user principal name, mail address, display name, or proxy address can all be reassigned, so a
/// match on any of them is not proof of the same object. These assertions are architectural: they
/// fail if such a search is ever added to the provider that feeds the Verified state.
/// </para>
/// </summary>
public sealed class CorrelationSearchSafetyTests
{
    [Fact]
    public void The_graph_provider_interface_declares_no_name_based_search()
    {
        var names = typeof(Contracts.Providers.IGraphProvider)
            .GetMethods()
            .Select(m => m.Name)
            .ToArray();

        foreach (var forbidden in new[] { "Upn", "Mail", "DisplayName", "ProxyAddress", "SamAccountName", "DistinguishedName" })
        {
            Assert.DoesNotContain(names, n =>
                n.StartsWith("Find", StringComparison.OrdinalIgnoreCase)
                && n.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_concrete_provider_declares_no_name_based_search()
    {
        var names = typeof(GraphCloudProvider)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();

        foreach (var forbidden in new[] { "Upn", "Mail", "DisplayName", "ProxyAddress", "SamAccountName" })
        {
            Assert.DoesNotContain(names, n => n.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_only_correlation_entry_point_takes_an_ad_subject_not_a_name()
    {
        var correlate = typeof(GraphCloudProvider)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == "CorrelateAsync");

        var parameters = correlate.GetParameters().Select(p => p.ParameterType.Name).ToArray();

        // A subject built from the verified AD object, a tenant verdict, optional consistency bytes.
        Assert.Contains("AdCorrelationSubject", parameters);
        Assert.Contains("TenantVerificationResult", parameters);
        Assert.DoesNotContain("String", parameters.Where(p => p is not "byte[]"));
    }

    [Fact]
    public void The_correlation_evaluator_exposes_no_name_only_entry_point()
    {
        var methods = typeof(Core.Correlation.CloudCorrelationEvaluator)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.Name)
            .ToArray();

        Assert.Equal(["Evaluate"], methods);
    }
}

/// <summary>
/// Explicit proof that a shared mutable name never produces a verified correlation. This is the
/// regression that keeps a tempting shortcut from ever passing review.
/// </summary>
public sealed class NameFallbackIsNotIdentityTests
{
    private static readonly Guid AdGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    private static Core.Correlation.AdCorrelationSubject Subject(
        string? upn = null, string? mail = null, string? displayName = null) => new()
        {
            ObjectGuid = AdGuid,
            DistinguishedName = "CN=User,OU=Lab,DC=example,DC=com",
            UserPrincipalName = upn,
            Mail = mail,
            DisplayName = displayName,
        };

    [Theory]
    [InlineData("same@example.test", null, null)]
    [InlineData(null, "same@example.test", null)]
    [InlineData(null, null, "Same Name")]
    public void A_shared_name_without_an_immutable_match_is_unmatched(
        string? upn,
        string? mail,
        string? displayName)
    {
        // The cloud batch is empty because the immutable-identifier query found nothing. A cloud user
        // that merely shares a name is not returned here, and even if it were, it could not verify.
        var result = Core.Correlation.CloudCorrelationEvaluator.Evaluate(
            Subject(upn, mail, displayName),
            [[]],
            tenantVerified: true);

        Assert.Equal(Contracts.Cloud.CorrelationOutcome.Unmatched, result.Outcome);
        Assert.Null(result.EntraObjectId);
    }

    [Fact]
    public void A_shared_proxy_address_alone_does_not_verify()
    {
        // Proxy addresses are not even an input to the evaluator; there is no member to supply one.
        var members = typeof(Core.Correlation.AdCorrelationSubject)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        Assert.DoesNotContain("ProxyAddresses", members);
        Assert.DoesNotContain("ProxyAddress", members);

        var result = Core.Correlation.CloudCorrelationEvaluator.Evaluate(
            Subject(),
            [[]],
            tenantVerified: true);

        Assert.Equal(Contracts.Cloud.CorrelationOutcome.Unmatched, result.Outcome);
    }

    [Fact]
    public void A_verified_result_requires_a_non_null_entra_object_id()
    {
        var verified = Contracts.Cloud.CloudCorrelationResult.Verified(Guid.NewGuid(), []);

        Assert.True(verified.IsVerified);
        Assert.NotEqual(Guid.Empty, verified.EntraObjectId);

        foreach (var refusal in new[]
        {
            Contracts.Cloud.CloudCorrelationResult.Unmatched([]),
            Contracts.Cloud.CloudCorrelationResult.Ambiguous([], 2),
            Contracts.Cloud.CloudCorrelationResult.Conflict([]),
            Contracts.Cloud.CloudCorrelationResult.TenantMismatch([]),
        })
        {
            Assert.False(refusal.IsVerified);
            Assert.Null(refusal.EntraObjectId);
        }
    }
}
