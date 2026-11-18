using ADEXO.Contracts.Authority;
using ADEXO.Contracts.Capabilities;
using ADEXO.Contracts.Concurrency;
using ADEXO.Contracts.Identity;
using ADEXO.Contracts.Operations;
using ADEXO.Contracts.Preview;
using ADEXO.Contracts.Results;
using ADEXO.Contracts.Validation;
using ADEXO.Core.Operations;
using ADEXO.Core.Tests.Support;

namespace ADEXO.Core.Tests;

public sealed class OperationStateMachineTests
{
    public static IEnumerable<object[]> AllPairs()
    {
        var expected = ExpectedPairs();
        foreach (OperationState from in Enum.GetValues<OperationState>())
        {
            foreach (OperationState to in Enum.GetValues<OperationState>())
            {
                yield return [from, to, expected.Contains((from, to))];
            }
        }
    }

    [Fact]
    public void Published_pairs_match_the_reviewed_set()
    {
        Assert.Equal(ExpectedPairs(), OperationStateMachine.LegalPairs.ToHashSet());
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Every_pair_is_accepted_only_when_defined(OperationState from, OperationState to, bool legal)
    {
        if (legal)
        {
            Assert.Equal(to, OperationStateMachine.Transition(from, to));
            return;
        }

        Assert.Throws<InvalidOperationException>(() => OperationStateMachine.Transition(from, to));
    }

    [Theory]
    [InlineData(OperationState.Succeeded)]
    [InlineData(OperationState.Failed)]
    [InlineData(OperationState.Canceled)]
    [InlineData(OperationState.RollbackSucceeded)]
    [InlineData(OperationState.RollbackFailed)]
    public void Terminal_states_have_no_exit(OperationState state)
    {
        Assert.True(OperationStateMachine.IsTerminal(state));
        Assert.DoesNotContain(OperationStateMachine.LegalPairs, pair => pair.From == state);
    }

    private static HashSet<(OperationState From, OperationState To)> ExpectedPairs() =>
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
}

public sealed class CancellationTests
{
    [Fact]
    public void Before_execution_cancels()
    {
        var operation = Sample();
        operation.MoveTo(OperationState.ReadingCurrentState);
        operation.MoveTo(OperationState.Validating);
        operation.MoveTo(OperationState.AwaitingConfirmation);
        operation.MoveTo(OperationState.Authorizing);
        operation.ApplyCancellation();
        Assert.Equal(OperationState.Canceled, operation.State);
    }

    [Fact]
    public void Execution_without_a_mutation_cancels_without_using_the_illegal_pair()
    {
        var operation = Sample();
        operation.MoveTo(OperationState.ReadingCurrentState);
        operation.MoveTo(OperationState.Validating);
        operation.MoveTo(OperationState.AwaitingConfirmation);
        operation.MoveTo(OperationState.Authorizing);
        operation.MoveTo(OperationState.Executing);
        Assert.Throws<InvalidOperationException>(() => operation.MoveTo(OperationState.Canceled));
        operation.ApplyCancellation();
        Assert.Equal(OperationState.Canceled, operation.State);
    }

    [Fact]
    public void Confirmed_mutation_becomes_partial()
    {
        var operation = Executing();
        operation.AddStep(new OperationStep("write", StepKind.Mutation, StepOutcome.Succeeded));
        operation.ApplyCancellation();
        Assert.Equal(OperationState.PartiallyCompleted, operation.State);
    }

    [Fact]
    public void Unknown_mutation_result_is_not_success_or_unknown_failure()
    {
        var operation = Executing();
        operation.AddStep(new OperationStep("write", StepKind.Mutation, StepOutcome.OutcomeUnknown));
        operation.ApplyCancellation();
        Assert.Equal(OperationState.VerificationFailed, operation.State);
        var result = OperationResult<string>.FromException(new OperationCanceledException());
        Assert.True(result.IsCanceled);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Verification_cancellation_keeps_history_and_does_not_claim_undo()
    {
        var operation = Executing();
        operation.AddStep(new OperationStep("write", StepKind.Mutation, StepOutcome.Succeeded));
        operation.MoveTo(OperationState.Verifying);
        operation.ApplyCancellation();
        Assert.Equal(OperationState.PartiallyCompleted, operation.State);
        Assert.Contains(operation.CompletedSteps, step => step.Outcome == StepOutcome.Succeeded);
        Assert.NotEqual(OperationState.Canceled, operation.State);
    }

    private static ChangeOperation Executing()
    {
        var operation = Sample();
        operation.MoveTo(OperationState.ReadingCurrentState);
        operation.MoveTo(OperationState.Validating);
        operation.MoveTo(OperationState.AwaitingConfirmation);
        operation.MoveTo(OperationState.Authorizing);
        operation.MoveTo(OperationState.Executing);
        return operation;
    }

    private static ChangeOperation Sample() => new(
        OperationId.New(),
        null,
        "alias",
        new AdObjectIdentity(Guid.NewGuid(), "CN=User,DC=example", "LDAP://CN=User,DC=example", "user", null),
        new DateTimeOffset(2026, 11, 17, 15, 0, 0, TimeSpan.Zero),
        "admin@example",
        ProviderKind.ActiveDirectory,
        new ChangePreview([]));
}

public sealed class GuardAndCompensationTests
{
    [Fact]
    public void Blocking_validation_prevents_execution_and_a_warning_does_not()
    {
        var blocking = new ValidationResult([new ValidationMessage(ValidationSeverity.Error, "alias", "Invalid alias.", "proxyAddresses", true)]);
        var warning = new ValidationResult([new ValidationMessage(ValidationSeverity.Warning, "alias", "Check the domain.", "proxyAddresses", false)]);
        var capability = ProviderCapability.ReadWrite(ProviderKind.ActiveDirectory, KnownFeature(), SourceOfAuthority.ActiveDirectory, Stamp());
        var preview = new ChangePreview([Item(editable: true, destructive: false, sensitive: false)]);

        Assert.False(ExecutionGuard.CanEnterExecuting(blocking, capability, preview));
        Assert.True(ExecutionGuard.CanEnterExecuting(warning, capability, preview));
        Assert.False(warning.HasBlockingErrors);
    }

    [Fact]
    public void Destructive_and_sensitive_previews_are_flagged_and_read_only_cannot_execute()
    {
        var destructive = new ChangePreview([Item(true, true, false)]);
        var sensitive = new ChangePreview([Item(true, false, true)]);
        Assert.True(destructive.Items.Single().IsDestructive);
        Assert.True(sensitive.RequiresConfirmation);
        var readOnly = new ChangePreview([Item(editable: false, destructive: false, sensitive: false)]);
        var capability = ProviderCapability.ReadWrite(ProviderKind.ExchangeOnline, KnownFeature(), SourceOfAuthority.ExchangeOnline, Stamp());
        Assert.False(ExecutionGuard.CanEnterExecuting(new ValidationResult([]), capability, readOnly));
    }

    [Fact]
    public void Success_stays_success_and_reversal_is_a_new_operation()
    {
        var clock = new FakeClock(Stamp());
        var operation = Reach(OperationState.Succeeded);
        operation.AllowCompensation();
        var reversal = CompensatingOperation.Create(operation, clock.UtcNow);
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Equal(OperationState.Created, reversal.State);
        Assert.Equal(operation.Id, reversal.ParentOperationId);
        Assert.NotEqual(operation.Id, reversal.Id);
        Assert.False(CompensationSafety.IsSafe(ConcurrencyToken.None, ConcurrencyToken.None));
        var observed = ConcurrencyToken.From(ConcurrencyTokenKind.ActiveDirectorySnapshot, "hash-1");
        Assert.False(CompensationSafety.IsSafe(observed, ConcurrencyToken.From(ConcurrencyTokenKind.ActiveDirectorySnapshot, "hash-2")));
        Assert.True(CompensationSafety.IsSafe(observed, ConcurrencyToken.From(ConcurrencyTokenKind.ActiveDirectorySnapshot, "hash-1")));
    }

    private static ChangeOperation Reach(OperationState target)
    {
        var operation = new ChangeOperation(
            OperationId.New(),
            null,
            "alias",
            new AdObjectIdentity(Guid.NewGuid(), "CN=User,DC=example", "LDAP://CN=User,DC=example", "user", null),
            Stamp(),
            "admin@example",
            ProviderKind.ActiveDirectory,
            new ChangePreview([Item(true, false, false)]));
        foreach (var state in new[]
                 {
                     OperationState.ReadingCurrentState,
                     OperationState.Validating,
                     OperationState.AwaitingConfirmation,
                     OperationState.Authorizing,
                     OperationState.Executing,
                     OperationState.Verifying,
                     OperationState.Succeeded,
                 })
        {
            operation.MoveTo(state);
            if (state == target)
            {
                break;
            }
        }

        return operation;
    }

    private static ChangePreviewItem Item(bool editable, bool destructive, bool sensitive) => new(
        ProviderKind.ActiveDirectory,
        "proxyAddresses",
        "smtp:old@example",
        "smtp:new@example",
        SourceOfAuthority.ActiveDirectory,
        "Change will synchronize through Entra Connect.",
        destructive || sensitive,
        destructive,
        sensitive,
        editable);

    private static ADEXO.Contracts.Features.KnownFeatureId KnownFeature() =>
        ADEXO.Contracts.Features.KnownFeatureId.ActiveDirectoryUserProxyAddresses;

    private static DateTimeOffset Stamp() => new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);
}
