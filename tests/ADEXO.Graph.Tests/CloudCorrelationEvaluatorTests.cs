using ADEXO.Contracts.Cloud;
using ADEXO.Core.Correlation;

namespace ADEXO.Graph.Tests;

public sealed class CloudCorrelationEvaluatorTests
{
    private static readonly Guid AdGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid EntraId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid EntraId2 = Guid.Parse("99999999-8888-7777-6666-555555555555");

    private static AdCorrelationSubject Subject(
        string? sid = null,
        string? dn = null,
        string? upn = null,
        string? mail = null,
        string? displayName = null) => new()
        {
            ObjectGuid = AdGuid,
            SecurityIdentifier = sid,
            DistinguishedName = dn,
            UserPrincipalName = upn,
            Mail = mail,
            DisplayName = displayName,
        };

    private static CloudDirectoryUser Cloud(
        Guid? id = null,
        string? sid = null,
        string? dn = null,
        string? upn = null,
        string? mail = null,
        string? displayName = null) => new()
        {
            EntraObjectId = id ?? EntraId,
            OnPremisesSecurityIdentifier = sid,
            OnPremisesDistinguishedName = dn,
            OnPremisesImmutableId = "matching",
            UserPrincipalName = upn,
            Mail = mail,
            DisplayName = displayName,
        };

    [Fact]
    public void One_immutable_match_without_corroboration_is_verified()
    {
        var result = CloudCorrelationEvaluator.Evaluate(Subject(), [[Cloud()]], tenantVerified: true);

        Assert.Equal(CorrelationOutcome.Verified, result.Outcome);
        Assert.Equal(EntraId, result.EntraObjectId);
    }

    [Fact]
    public void No_matches_is_unmatched_not_an_error()
    {
        var result = CloudCorrelationEvaluator.Evaluate(Subject(), [[]], tenantVerified: true);

        Assert.Equal(CorrelationOutcome.Unmatched, result.Outcome);
        Assert.Null(result.EntraObjectId);
    }

    [Fact]
    public void The_same_entra_object_from_two_candidates_is_one_match()
    {
        var cloud = Cloud();

        var result = CloudCorrelationEvaluator.Evaluate(Subject(), [[cloud], [cloud]], tenantVerified: true);

        Assert.Equal(CorrelationOutcome.Verified, result.Outcome);
        Assert.Equal(EntraId, result.EntraObjectId);
    }

    [Fact]
    public void Two_distinct_entra_objects_is_ambiguous()
    {
        var result = CloudCorrelationEvaluator.Evaluate(
            Subject(),
            [[Cloud()], [Cloud(id: EntraId2)]],
            tenantVerified: true);

        Assert.Equal(CorrelationOutcome.Ambiguous, result.Outcome);
        Assert.Null(result.EntraObjectId);
        Assert.Contains("will not choose", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Agreeing_security_identifier_corroborates()
    {
        var subject = Subject(sid: "S-1-5-21-1-2-3-1103");
        var result = CloudCorrelationEvaluator.Evaluate(
            subject,
            [[Cloud(sid: "S-1-5-21-1-2-3-1103")]],
            tenantVerified: true);

        Assert.Equal(CorrelationOutcome.Verified, result.Outcome);
        Assert.Contains(result.Evidence, e => e.Kind == CorrelationEvidenceKind.SecurityIdentifier && e.Agreed);
    }

    [Fact]
    public void Conflicting_security_identifier_is_a_conflict_not_a_match()
    {
        var subject = Subject(sid: "S-1-5-21-1-2-3-1103");
        var result = CloudCorrelationEvaluator.Evaluate(
            subject,
            [[Cloud(sid: "S-1-5-21-9-9-9-9999")]],
            tenantVerified: true);

        Assert.Equal(CorrelationOutcome.Conflict, result.Outcome);
        Assert.Null(result.EntraObjectId);
    }

    [Fact]
    public void Agreeing_distinguished_name_corroborates()
    {
        var subject = Subject(dn: "CN=User,OU=Lab,DC=example,DC=com");
        var result = CloudCorrelationEvaluator.Evaluate(
            subject,
            [[Cloud(dn: "CN=User,OU=Lab,DC=example,DC=com")]],
            tenantVerified: true);

        Assert.Equal(CorrelationOutcome.Verified, result.Outcome);
        Assert.Contains(result.Evidence, e => e.Kind == CorrelationEvidenceKind.DistinguishedName && e.Agreed);
    }

    [Fact]
    public void Conflicting_distinguished_name_is_a_conflict()
    {
        var subject = Subject(dn: "CN=User,OU=Lab,DC=example,DC=com");
        var result = CloudCorrelationEvaluator.Evaluate(
            subject,
            [[Cloud(dn: "CN=Someone,OU=Other,DC=example,DC=com")]],
            tenantVerified: true);

        Assert.Equal(CorrelationOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public void A_matching_user_principal_name_alone_never_verifies()
    {
        // The cloud object was returned by an immutable-id query, but here the batch is empty and the
        // only thing that "matches" is the mutable name, which must not produce a match.
        var subject = Subject(upn: "shared@example.com");

        var result = CloudCorrelationEvaluator.Evaluate(subject, [[]], tenantVerified: true);

        Assert.Equal(CorrelationOutcome.Unmatched, result.Outcome);
    }

    [Fact]
    public void A_matching_mail_address_alone_never_verifies()
    {
        var subject = Subject(mail: "shared@example.com");

        var result = CloudCorrelationEvaluator.Evaluate(subject, [[]], tenantVerified: true);

        Assert.Equal(CorrelationOutcome.Unmatched, result.Outcome);
    }

    [Fact]
    public void A_matching_display_name_alone_never_verifies()
    {
        var subject = Subject(displayName: "Shared Name");

        var result = CloudCorrelationEvaluator.Evaluate(subject, [[]], tenantVerified: true);

        Assert.Equal(CorrelationOutcome.Unmatched, result.Outcome);
    }

    [Fact]
    public void A_mutable_name_match_is_recorded_as_diagnostic_evidence_only()
    {
        var subject = Subject(upn: "Shared@Example.com");

        var result = CloudCorrelationEvaluator.Evaluate(subject, [[Cloud(upn: "shared@example.com")]], tenantVerified: true);

        // The outcome is driven by the immutable identifier, and the name match is merely noted.
        Assert.Equal(CorrelationOutcome.Verified, result.Outcome);
        var diagnostic = Assert.Single(result.Evidence, e => e.Kind == CorrelationEvidenceKind.MutableName);
        Assert.Contains("diagnostic only", diagnostic.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_mutable_name_never_overrides_an_ambiguous_result()
    {
        var subject = Subject(upn: "same@example.com");

        var result = CloudCorrelationEvaluator.Evaluate(
            subject,
            [[Cloud(upn: "same@example.com")], [Cloud(id: EntraId2, upn: "same@example.com")]],
            tenantVerified: true);

        Assert.Equal(CorrelationOutcome.Ambiguous, result.Outcome);
    }

    [Fact]
    public void A_failed_tenant_lock_refuses_without_evaluating()
    {
        var result = CloudCorrelationEvaluator.Evaluate(Subject(), [[Cloud()]], tenantVerified: false);

        Assert.Equal(CorrelationOutcome.TenantMismatch, result.Outcome);
        Assert.Null(result.EntraObjectId);
    }

    [Fact]
    public void Absent_corroborating_values_are_not_a_conflict()
    {
        // Only one side carries a SID. That is "cannot corroborate", not "contradicts".
        var subject = Subject(sid: "S-1-5-21-1-2-3-1103");
        var result = CloudCorrelationEvaluator.Evaluate(subject, [[Cloud()]], tenantVerified: true);

        Assert.Equal(CorrelationOutcome.Verified, result.Outcome);
    }
}
