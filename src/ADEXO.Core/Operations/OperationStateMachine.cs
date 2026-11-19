using ADEXO.Contracts.Operations;

namespace ADEXO.Core.Operations;

public static class OperationStateMachine
{
    private static readonly HashSet<(OperationState From, OperationState To)> Legal =
    [
        (OperationState.Created, OperationState.ReadingCurrentState),
        (OperationState.Created, OperationState.Canceled),
        (OperationState.ReadingCurrentState, OperationState.Validating),
        (OperationState.ReadingCurrentState, OperationState.Failed),
        (OperationState.ReadingCurrentState, OperationState.Canceled),
        (OperationState.Validating, OperationState.AwaitingConfirmation),
        (OperationState.Validating, OperationState.Failed),
        (OperationState.Validating, OperationState.Canceled),
        (OperationState.AwaitingConfirmation, OperationState.Authorizing),
        (OperationState.AwaitingConfirmation, OperationState.Canceled),
        (OperationState.Authorizing, OperationState.Executing),
        (OperationState.Authorizing, OperationState.Failed),
        (OperationState.Authorizing, OperationState.Canceled),
        (OperationState.Executing, OperationState.Verifying),
        (OperationState.Executing, OperationState.PartiallyCompleted),
        (OperationState.Executing, OperationState.VerificationFailed),
        (OperationState.Executing, OperationState.Failed),
        (OperationState.Verifying, OperationState.Succeeded),
        (OperationState.Verifying, OperationState.VerificationFailed),
        (OperationState.Verifying, OperationState.PartiallyCompleted),
        (OperationState.PartiallyCompleted, OperationState.RollbackAvailable),
        (OperationState.PartiallyCompleted, OperationState.Failed),
        (OperationState.VerificationFailed, OperationState.RollbackAvailable),
        (OperationState.VerificationFailed, OperationState.Failed),
        (OperationState.RollbackAvailable, OperationState.RollingBack),
        (OperationState.RollingBack, OperationState.RollbackSucceeded),
        (OperationState.RollingBack, OperationState.RollbackFailed),
    ];

    public static IReadOnlyCollection<(OperationState From, OperationState To)> LegalPairs => Legal;

    public static bool IsTerminal(OperationState state) =>
        state is OperationState.Succeeded
            or OperationState.Failed
            or OperationState.Canceled
            or OperationState.RollbackSucceeded
            or OperationState.RollbackFailed;

    public static bool IsLegal(OperationState from, OperationState to) => Legal.Contains((from, to));

    public static OperationState Transition(OperationState from, OperationState to)
    {
        if (!IsLegal(from, to))
        {
            throw new InvalidOperationException($"Transition {from} -> {to} is not legal.");
        }

        return to;
    }
}
