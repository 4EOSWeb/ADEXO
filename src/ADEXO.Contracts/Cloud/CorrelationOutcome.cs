namespace ADEXO.Contracts.Cloud;

/// <summary>
/// How an Active Directory object was related to a Microsoft Entra object.
/// Only <see cref="Verified"/> permits cloud user data to be presented as belonging to the
/// selected AD object. Every other outcome is a deliberate refusal to merge.
/// </summary>
public enum CorrelationOutcome
{
    /// <summary>Not evaluated yet, because a tenant lock has not passed or no AD identity exists.</summary>
    NotEvaluated,

    /// <summary>
    /// Exactly one Entra object matched exactly one approved immutable identifier, and the
    /// corroborating evidence did not contradict it.
    /// </summary>
    Verified,

    /// <summary>
    /// No Entra object matched any valid immutable identifier. This is a legitimate result: the
    /// environments may simply not be synchronized. It is not an error.
    /// </summary>
    Unmatched,

    /// <summary>More than one distinct Entra object survived candidate evaluation.</summary>
    Ambiguous,

    /// <summary>A candidate matched, but strong corroborating evidence explicitly contradicted it.</summary>
    Conflict,

    /// <summary>A tenant lock failed, so no correlation was attempted.</summary>
    TenantMismatch,
}

/// <summary>Why a particular piece of evidence was accepted, rejected, or treated as diagnostic.</summary>
public enum CorrelationEvidenceKind
{
    /// <summary>An immutable identifier derived from the verified AD object. This is the only positive key.</summary>
    ImmutableId,

    /// <summary>The on-premises security identifier. Corroborating only; never sufficient alone.</summary>
    SecurityIdentifier,

    /// <summary>The on-premises distinguished name. Corroborating only; never sufficient alone.</summary>
    DistinguishedName,

    /// <summary>A mutable attribute that happened to match. Diagnostic only, and never sufficient.</summary>
    MutableName,
}

/// <summary>
/// One piece of correlation evidence. The values here are recorded so a refusal can be explained,
/// not so a match can be manufactured from them.
/// </summary>
public sealed class CorrelationEvidence
{
    public CorrelationEvidence(CorrelationEvidenceKind kind, bool agreed, string? detail)
    {
        Kind = kind;
        Agreed = agreed;
        Detail = detail;
    }

    public CorrelationEvidenceKind Kind { get; }

    /// <summary>
    /// True when the cloud and AD values agree. For a mutable-name check this is recorded for
    /// diagnostics only and is never counted toward a <see cref="CorrelationOutcome.Verified"/> result.
    /// </summary>
    public bool Agreed { get; }

    public string? Detail { get; }
}

/// <summary>
/// The evaluated relationship between the selected AD object and an Entra object.
/// </summary>
public sealed class CloudCorrelationResult
{
    private CloudCorrelationResult(
        CorrelationOutcome outcome,
        Guid? entraObjectId,
        IReadOnlyList<CorrelationEvidence> evidence,
        string summary)
    {
        Outcome = outcome;
        EntraObjectId = entraObjectId;
        Evidence = evidence;
        Summary = summary;
    }

    public CorrelationOutcome Outcome { get; }

    /// <summary>Present only when <see cref="Outcome"/> is <see cref="CorrelationOutcome.Verified"/>.</summary>
    public Guid? EntraObjectId { get; }

    public IReadOnlyList<CorrelationEvidence> Evidence { get; }

    /// <summary>A sanitized explanation suitable for display.</summary>
    public string Summary { get; }

    public bool IsVerified => Outcome == CorrelationOutcome.Verified && EntraObjectId is not null;

    public static CloudCorrelationResult Verified(Guid entraObjectId, IReadOnlyList<CorrelationEvidence> evidence) =>
        new(CorrelationOutcome.Verified, entraObjectId, evidence, "The cloud object is correlated to the selected user by a synchronized identifier.");

    public static CloudCorrelationResult Unmatched(IReadOnlyList<CorrelationEvidence> evidence) =>
        new(CorrelationOutcome.Unmatched, null, evidence, "No cloud object carries a synchronized identifier for this user. The environments may not be synchronized.");

    public static CloudCorrelationResult Ambiguous(IReadOnlyList<CorrelationEvidence> evidence, int candidateCount) =>
        new(CorrelationOutcome.Ambiguous, null, evidence, $"{candidateCount} cloud objects matched the synchronized identifier. ADEXO will not choose between them.");

    public static CloudCorrelationResult Conflict(IReadOnlyList<CorrelationEvidence> evidence) =>
        new(CorrelationOutcome.Conflict, null, evidence, "A cloud object matched the synchronized identifier, but the corroborating evidence contradicts it.");

    public static CloudCorrelationResult TenantMismatch(IReadOnlyList<CorrelationEvidence> evidence) =>
        new(CorrelationOutcome.TenantMismatch, null, evidence, "The tenant lock failed, so no correlation was attempted.");

    public static CloudCorrelationResult NotEvaluated(IReadOnlyList<CorrelationEvidence> evidence) =>
        new(CorrelationOutcome.NotEvaluated, null, evidence, "Correlation has not been evaluated.");
}
