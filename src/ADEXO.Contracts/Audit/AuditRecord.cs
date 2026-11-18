using ADEXO.Contracts.Capabilities;
using ADEXO.Contracts.Operations;
using ADEXO.Contracts.Secrets;

namespace ADEXO.Contracts.Audit;

public sealed class AuditRecord
{
    public required OperationId OperationId { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }

    public string? Administrator { get; init; }

    public string? Machine { get; init; }

    public string? Tenant { get; init; }

    public Guid? TargetObjectGuid { get; init; }

    public Guid? CloudObjectId { get; init; }

    public ProviderKind Provider { get; init; }

    public string Action { get; init; } = string.Empty;

    public string? BeforeState { get; init; }

    public string? RequestedState { get; init; }

    public string? VerifiedState { get; init; }

    public string Result { get; init; } = string.Empty;

    public string? CorrelationId { get; init; }

    public string? RollbackResult { get; init; }

    public SecretText? ProtectedValue { get; init; }

    public static string Redacted(SecretText secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return secret.ToString();
    }
}

public interface IAuditSink
{
    void Record(AuditRecord record);
}
