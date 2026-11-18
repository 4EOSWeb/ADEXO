namespace ADEXO.Contracts.Operations;

public enum OperationState
{
    Created,
    ReadingCurrentState,
    Validating,
    AwaitingConfirmation,
    Authorizing,
    Executing,
    Verifying,
    Succeeded,
    Failed,
    Canceled,
    PartiallyCompleted,
    VerificationFailed,
    RollbackAvailable,
    RollingBack,
    RollbackSucceeded,
    RollbackFailed,
}

public enum StepKind
{
    Read,
    Mutation,
    Verification,
    Compensation,
}

public enum StepOutcome
{
    Planned,
    Started,
    Succeeded,
    Failed,
    CanceledBeforeExecution,
    OutcomeUnknown,
    Compensated,
    CompensationFailed,
}

public readonly record struct OperationId
{
    private OperationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static OperationId New() => new(Guid.NewGuid());

    public static OperationId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An operation id is required.", nameof(value));
        }

        return new OperationId(value);
    }

    public override string ToString() => Value.ToString("D");
}

public sealed record OperationStep(string Name, StepKind Kind, StepOutcome Outcome);
