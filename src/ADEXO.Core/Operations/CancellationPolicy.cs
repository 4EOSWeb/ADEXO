using ADEXO.Contracts.Operations;

namespace ADEXO.Core.Operations;

public readonly record struct CancellationDecision(OperationState NextState, bool MutationAttempted, bool OutcomeIndeterminate);

public static class CancellationPolicy
{
    public static bool MutationWasStarted(IEnumerable<OperationStep> steps) =>
        steps.Any(step => step.Kind == StepKind.Mutation && IsAttempt(step.Outcome));

    public static CancellationDecision Decide(OperationState state, IReadOnlyList<OperationStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (OperationStateMachine.IsTerminal(state))
        {
            throw new InvalidOperationException("A terminal operation is not reopened.");
        }

        var mutations = steps.Where(step => step.Kind == StepKind.Mutation).ToArray();
        var started = mutations.Any(step => IsAttempt(step.Outcome));
        var confirmed = mutations.Any(step => step.Outcome == StepOutcome.Succeeded);
        var unknown = mutations.Any(step => step.Outcome is StepOutcome.OutcomeUnknown or StepOutcome.Started);

        if (state == OperationState.Verifying)
        {
            var next = confirmed ? OperationState.PartiallyCompleted : OperationState.VerificationFailed;
            return new CancellationDecision(next, started, unknown);
        }

        if (!started)
        {
            return new CancellationDecision(OperationState.Canceled, false, false);
        }

        if (unknown && !confirmed)
        {
            return new CancellationDecision(OperationState.VerificationFailed, true, true);
        }

        return new CancellationDecision(OperationState.PartiallyCompleted, true, unknown);
    }

    private static bool IsAttempt(StepOutcome outcome) =>
        outcome is StepOutcome.Started
            or StepOutcome.Succeeded
            or StepOutcome.Failed
            or StepOutcome.OutcomeUnknown
            or StepOutcome.Compensated
            or StepOutcome.CompensationFailed;
}
