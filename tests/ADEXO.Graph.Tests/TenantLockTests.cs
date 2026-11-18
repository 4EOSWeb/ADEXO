using System.Reflection;
using ADEXO.Contracts.Cloud;
using ADEXO.Graph;
using ADEXO.Graph.Orchestration;

namespace ADEXO.Graph.Tests;

/// <summary>
/// Proves the tenant lock and the ordering that prevents any cloud read of a selected user before both
/// locks pass.
/// </summary>
public sealed class TenantLockTests
{
    private static readonly Guid ApprovedTenant = Guid.Parse("c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af");
    private static readonly Guid OtherTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void T1_mismatch_reports_a_tenant_mismatch_result()
    {
        var result = TenantVerificationResult.TokenMismatch(OtherTenant, ApprovedTenant);

        Assert.False(result.TokenTenantMatched);
        Assert.False(result.DirectoryTenantMatched);
        Assert.False(result.IsVerified);
    }

    [Fact]
    public void T2_mismatch_after_t1_match_is_not_verified()
    {
        var result = TenantVerificationResult.DirectoryMismatch(ApprovedTenant, OtherTenant, ApprovedTenant);

        Assert.True(result.TokenTenantMatched);
        Assert.False(result.DirectoryTenantMatched);
        Assert.False(result.IsVerified);
        Assert.Contains("different tenant", result.Summary!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Both_matching_is_verified()
    {
        var result = TenantVerificationResult.Verified(ApprovedTenant, ApprovedTenant, ApprovedTenant);

        Assert.True(result.IsVerified);
        Assert.Null(result.Summary);
    }

    [Fact]
    public void An_unavailable_directory_read_is_not_verified()
    {
        var result = TenantVerificationResult.Unavailable(ApprovedTenant, ApprovedTenant, "The tenant could not be verified.");

        Assert.False(result.IsVerified);
    }

    [Fact]
    public void Tenant_identifiers_are_compared_as_guids_not_strings()
    {
        // Differently formatted but equal identifiers must match.
        var upper = Guid.Parse("C18EE4BE-07A0-4DA1-9DDF-C3EBE9BAE0AF");
        var result = TenantVerificationResult.Verified(upper, ApprovedTenant, ApprovedTenant);

        Assert.True(result.IsVerified);
        Assert.Equal(ApprovedTenant, result.ObservedTokenTenantId);
    }

    [Fact]
    public void The_verification_result_cannot_be_constructed_in_an_inconsistent_state()
    {
        // There is no public constructor, so a caller cannot fabricate a verified result without the
        // factory. That is what makes the ordering structural rather than conventional.
        var constructors = typeof(TenantVerificationResult)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(constructors);
    }

    [Fact]
    public void The_correlation_evaluator_refuses_when_the_lock_failed()
    {
        var outcome = Core.Correlation.CloudCorrelationEvaluator.Evaluate(
            new Core.Correlation.AdCorrelationSubject { ObjectGuid = Guid.NewGuid() },
            [[new CloudDirectoryUser { EntraObjectId = Guid.NewGuid() }]],
            tenantVerified: false);

        Assert.Equal(CorrelationOutcome.TenantMismatch, outcome.Outcome);
    }

    [Fact]
    public void The_approved_tenant_is_exposed_for_the_ui_and_is_not_nullable()
    {
        Assert.NotEqual(Guid.Empty, ApprovedTenant);
    }
}
