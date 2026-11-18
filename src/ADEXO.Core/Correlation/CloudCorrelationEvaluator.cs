using ADEXO.Contracts.Cloud;

namespace ADEXO.Core.Correlation;

/// <summary>
/// The Active Directory side of a correlation decision, reduced to the evidence that may corroborate
/// but never create a match.
/// </summary>
public sealed class AdCorrelationSubject
{
    public required Guid ObjectGuid { get; init; }

    /// <summary>The object's on-premises security identifier, when known.</summary>
    public string? SecurityIdentifier { get; init; }

    /// <summary>The object's distinguished name, when known.</summary>
    public string? DistinguishedName { get; init; }

    /// <summary>Mutable attributes, recorded for diagnostics only. These can never produce a match.</summary>
    public string? UserPrincipalName { get; init; }

    public string? Mail { get; init; }

    public string? DisplayName { get; init; }
}

/// <summary>
/// Evaluates whether a set of cloud users is the same object as the selected Active Directory user.
/// <para>
/// The only positive key is a synchronized immutable identifier. Every other attribute is
/// corroborating or diagnostic. A shared user principal name, mail address, display name, proxy
/// address, or distinguished name is recorded and can never on its own produce
/// <see cref="CorrelationOutcome.Verified"/>, because all of them are mutable and reassignable.
/// </para>
/// </summary>
public static class CloudCorrelationEvaluator
{
    /// <summary>
    /// Evaluates the correlation.
    /// </summary>
    /// <param name="subject">The verified Active Directory object.</param>
    /// <param name="candidates">Candidates matched by immutable identifier, one entry per candidate query result.</param>
    /// <param name="tenantVerified">Whether both tenant locks held. A false value short-circuits to a refusal.</param>
    public static CloudCorrelationResult Evaluate(
        AdCorrelationSubject subject,
        IReadOnlyList<IReadOnlyList<CloudDirectoryUser>> candidates,
        bool tenantVerified)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(candidates);

        if (!tenantVerified)
        {
            return CloudCorrelationResult.TenantMismatch([]);
        }

        // Collapse the per-candidate results into distinct cloud objects. The same Entra object can
        // legitimately appear under more than one candidate; that is one object, not two.
        var distinct = new Dictionary<Guid, CloudDirectoryUser>();
        foreach (var batch in candidates)
        {
            foreach (var user in batch)
            {
                distinct[user.EntraObjectId] = user;
            }
        }

        var evidence = new List<CorrelationEvidence>();

        if (distinct.Count == 0)
        {
            AddDiagnosticMatches(subject, [], evidence);
            return CloudCorrelationResult.Unmatched(evidence);
        }

        if (distinct.Count > 1)
        {
            foreach (var user in distinct.Values)
            {
                evidence.Add(new CorrelationEvidence(
                    CorrelationEvidenceKind.ImmutableId,
                    true,
                    $"Cloud object {user.EntraObjectId} carries a matching synchronized identifier."));
            }

            return CloudCorrelationResult.Ambiguous(evidence, distinct.Count);
        }

        var matched = distinct.Values.Single();

        evidence.Add(new CorrelationEvidence(
            CorrelationEvidenceKind.ImmutableId,
            true,
            $"Cloud object {matched.EntraObjectId} carries a matching synchronized identifier."));

        // Corroboration can reject a match. It can never create one, which is why it is evaluated
        // only after a unique immutable-identifier match already exists. A value present on one side
        // and absent on the other cannot corroborate, but it does not contradict either: absence of
        // evidence is not evidence of a different object.
        var conflict = false;

        if (!string.IsNullOrWhiteSpace(subject.SecurityIdentifier)
            && !string.IsNullOrWhiteSpace(matched.OnPremisesSecurityIdentifier))
        {
            var agreed = string.Equals(subject.SecurityIdentifier, matched.OnPremisesSecurityIdentifier, StringComparison.OrdinalIgnoreCase);

            evidence.Add(new CorrelationEvidence(
                CorrelationEvidenceKind.SecurityIdentifier,
                agreed,
                agreed
                    ? "The on-premises security identifier agrees."
                    : "The on-premises security identifier does not agree."));

            if (!agreed)
            {
                conflict = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(subject.DistinguishedName)
            && !string.IsNullOrWhiteSpace(matched.OnPremisesDistinguishedName))
        {
            var agreed = string.Equals(subject.DistinguishedName, matched.OnPremisesDistinguishedName, StringComparison.OrdinalIgnoreCase);

            evidence.Add(new CorrelationEvidence(
                CorrelationEvidenceKind.DistinguishedName,
                agreed,
                agreed
                    ? "The on-premises distinguished name agrees."
                    : "The on-premises distinguished name does not agree."));

            if (!agreed)
            {
                conflict = true;
            }
        }

        AddDiagnosticMatches(subject, [matched], evidence);

        return conflict
            ? CloudCorrelationResult.Conflict(evidence)
            : CloudCorrelationResult.Verified(matched.EntraObjectId, evidence);
    }

    /// <summary>
    /// Records whether mutable attributes happened to agree. This is diagnostic output only. Nothing
    /// here can influence the outcome, and its absence is not a conflict.
    /// </summary>
    private static void AddDiagnosticMatches(
        AdCorrelationSubject subject,
        IReadOnlyList<CloudDirectoryUser> users,
        List<CorrelationEvidence> evidence)
    {
        var checks = new (string? AdValue, Func<CloudDirectoryUser, string?> CloudValue, string Label)[]
        {
            (subject.UserPrincipalName, u => u.UserPrincipalName, "user principal name"),
            (subject.Mail, u => u.Mail, "mail address"),
            (subject.DisplayName, u => u.DisplayName, "display name"),
        };

        foreach (var (adValue, cloudValue, label) in checks)
        {
            if (string.IsNullOrWhiteSpace(adValue))
            {
                continue;
            }

            var agreed = users.Any(u =>
                string.Equals(adValue, cloudValue(u), StringComparison.OrdinalIgnoreCase));

            if (!agreed)
            {
                continue;
            }

            evidence.Add(new CorrelationEvidence(
                CorrelationEvidenceKind.MutableName,
                true,
                $"The {label} happens to match. This is diagnostic only and did not contribute to the outcome."));
        }
    }
}
