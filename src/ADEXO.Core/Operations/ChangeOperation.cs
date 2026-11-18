using ADEXO.Contracts.Capabilities;
using ADEXO.Contracts.Concurrency;
using ADEXO.Contracts.Identity;
using ADEXO.Contracts.Operations;
using ADEXO.Contracts.Preview;
using ADEXO.Contracts.Results;

namespace ADEXO.Core.Operations;

public sealed class ChangeOperation
{
    private readonly List<OperationStep> _steps = [];

    public ChangeOperation(
        OperationId id,
        OperationId? parentOperationId,
        string operationName,
        AdObjectIdentity target,
        DateTimeOffset createdUtc,
        string? requestedBy,
        ProviderKind provider,
        ChangePreview plannedChanges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(plannedChanges);
        Id = id;
        ParentOperationId = parentOperationId;
        OperationName = operationName;
        Target = target;
        CreatedUtc = createdUtc;
        RequestedBy = requestedBy;
        Provider = provider;
        PlannedChanges = plannedChanges;
        State = OperationState.Created;
    }

    public OperationId Id { get; }

    public OperationId? ParentOperationId { get; }

    public string OperationName { get; }

    public AdObjectIdentity Target { get; }

    public DateTimeOffset CreatedUtc { get; }

    public string? RequestedBy { get; }

    public OperationState State { get; private set; }

    public ProviderKind Provider { get; }

    public ChangePreview PlannedChanges { get; }

    public IReadOnlyList<OperationStep> CompletedSteps => _steps;

    public VerificationResult? Verification { get; private set; }

    public OperationError? Error { get; private set; }

    public bool HasCompensatingOperation { get; private set; }

    public ConcurrencyToken ObservedToken { get; private set; } = ConcurrencyToken.None;

    public bool MutatingStepStarted => CancellationPolicy.MutationWasStarted(_steps);

    public void AddStep(OperationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Add(step);
    }

    public void Observe(ConcurrencyToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        ObservedToken = token;
    }

    public void MoveTo(OperationState next) => State = OperationStateMachine.Transition(State, next);

    public void AllowCompensation()
    {
        if (State != OperationState.Succeeded)
        {
            throw new InvalidOperationException("Compensation is recorded only after success.");
        }

        HasCompensatingOperation = true;
    }

    public void RecordVerification(VerificationResult verification) => Verification = verification;

    public void RecordError(OperationError error) => Error = error;

    public void ApplyCancellation()
    {
        var decision = CancellationPolicy.Decide(State, _steps);
        if (decision.NextState == OperationState.Canceled && State == OperationState.Executing)
        {
            if (MutatingStepStarted)
            {
                throw new InvalidOperationException("Cancellation after a mutation is not Canceled.");
            }

            State = OperationState.Canceled;
            return;
        }

        MoveTo(decision.NextState);
    }
}

public static class CompensatingOperation
{
    public static ChangeOperation Create(ChangeOperation parent, DateTimeOffset createdUtc)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.State != OperationState.Succeeded || !parent.HasCompensatingOperation)
        {
            throw new InvalidOperationException("A reversal requires a succeeded operation that offers compensation.");
        }

        return new ChangeOperation(
            OperationId.New(),
            parent.Id,
            parent.OperationName + " reversal",
            parent.Target,
            createdUtc,
            parent.RequestedBy,
            parent.Provider,
            parent.PlannedChanges);
    }
}

public static class CompensationSafety
{
    public static bool IsSafe(ConcurrencyToken observed, ConcurrencyToken current)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(current);
        if (observed.Kind == ConcurrencyTokenKind.None || current.Kind == ConcurrencyTokenKind.None)
        {
            return false;
        }

        return observed.Equals(current);
    }
}
