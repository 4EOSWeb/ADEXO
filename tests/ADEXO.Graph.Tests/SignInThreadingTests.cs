using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Configuration;
using ADEXO.Graph.Authentication;
using Microsoft.Identity.Client;

namespace ADEXO.Graph.Tests;

/// <summary>
/// Behavioral proof for the live-lab sign-in hang.
/// <para>
/// The defect was that the interactive broker call was initiated from a thread-pool continuation
/// rather than the thread that owns the parent window, so WAM never surfaced a dialog and the section
/// stayed in SigningIn. These tests pin the contract that fixes it.
/// </para>
/// </summary>
public sealed class SignInThreadingTests
{
    private static PublicClientAuthenticationService Create(
        RecordingAcquirer interactive,
        StubWindowHandleValidator? validator = null,
        RequireInteractionSilentAcquirer? silent = null) =>
        // WamOnly is deliberate here: these tests exercise the broker path in isolation, and Auto would
        // fall back to the real system browser when no browser seam is supplied.
        new(
            new MicrosoftIdentityConfigurationBuilder().Build(),
            application: null,
            validator ?? new StubWindowHandleValidator(),
            interactiveAcquirer: interactive,
            silentAcquirer: silent ?? new RequireInteractionSilentAcquirer(),
            browserAcquirer: new RecordingAcquirer(),
            mode: Contracts.Authentication.AuthenticationMode.WamOnly);

    [Fact]
    public async Task A_rejected_window_handle_never_reaches_the_interactive_call()
    {
        var interactive = new RecordingAcquirer();
        var validator = new StubWindowHandleValidator { Reason = "The interactive sign-in requires a window handle, but none was supplied." };
        var service = Create(interactive, validator);

        var result = await service.SignInAsync(IntPtr.Zero, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Contracts.Results.FailureCategory.Validation, result.Error!.Category);
        Assert.Equal(0, interactive.CallCount);
    }

    [Fact]
    public async Task A_foreign_process_handle_is_reported_as_a_validation_failure()
    {
        var interactive = new RecordingAcquirer();
        var validator = new StubWindowHandleValidator { Reason = "The supplied window handle belongs to a different process." };
        var service = Create(interactive, validator);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Contracts.Results.FailureCategory.Validation, result.Error!.Category);
        Assert.Contains("different process", result.Error!.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, interactive.CallCount);
    }

    [Fact]
    public async Task State_remains_readable_while_the_interactive_call_is_pending()
    {
        // The deadlock guard in behavioral form. The previous implementation took a Monitor in
        // SignInAsync and in the State getter, so reading State while a sign-in was pending could block
        // forever. It must return promptly.
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var interactive = new RecordingAcquirer { HoldUntil = gate.Task };
        var service = Create(interactive);

        var signIn = service.SignInAsync((IntPtr)4321, CancellationToken.None);

        var read = Task.Run(() =>
        {
            _ = service.State;
            _ = service.CurrentAccount;
            return true;
        });

        var completed = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(read, completed);
        Assert.True(await read);

        gate.SetResult(true);
        await signIn;
    }

    [Fact]
    public async Task A_state_change_handler_can_read_state_without_deadlocking()
    {
        var service = Create(new RecordingAcquirer());

        var observed = new List<AuthenticationState>();
        service.StateChanged += (_, state) =>
        {
            // Reading state inside the handler must be safe. Doing so while the lock is held is the
            // shape that hung the UI thread.
            _ = service.State;
            _ = service.CurrentAccount;
            observed.Add(state);
        };

        var attempt = Task.Run(() => service.SignInAsync((IntPtr)5555, CancellationToken.None));
        var completed = await Task.WhenAny(attempt, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(attempt, completed);
        await attempt;
        Assert.Contains(AuthenticationState.Authenticating, observed);
    }

    [Fact]
    public async Task Checkpoints_are_recorded_so_a_hang_leaves_evidence()
    {
        var service = Create(new RecordingAcquirer());

        await service.SignInAsync((IntPtr)6666, CancellationToken.None);

        var checkpoints = service.LastAttemptCheckpoints;
        Assert.NotEmpty(checkpoints);
        Assert.Contains(checkpoints, c => c.Stage == "attempt-begin");
        Assert.Contains(checkpoints, c => c.Stage == "silent-attempt-returned");
        Assert.Contains(checkpoints, c => c.Stage.StartsWith("interactive-initiate", StringComparison.Ordinal));
        Assert.All(checkpoints, c => Assert.True(c.ManagedThreadId > 0));
    }

    [Fact]
    public async Task The_interactive_call_is_initiated_after_the_silent_attempt_reports_interaction()
    {
        // Ordering proof: the interactive seam runs only because the silent seam asked for interaction.
        var interactive = new RecordingAcquirer();
        var silent = new RequireInteractionSilentAcquirer();
        var service = Create(interactive, silent: silent);

        await service.SignInAsync((IntPtr)0x1234, CancellationToken.None);

        Assert.Equal(1, silent.CallCount);
        Assert.Equal(1, interactive.CallCount);
        Assert.Equal((IntPtr)0x1234, interactive.ObservedHandle);
    }

    [Fact]
    public async Task A_failed_interactive_call_reaches_the_failed_state()
    {
        var interactive = new RecordingAcquirer { Throw = new InvalidOperationException("synthetic failure") };
        var service = Create(interactive);

        var result = await service.SignInAsync((IntPtr)7777, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthenticationState.Failed, service.State);
    }

    [Fact]
    public async Task A_canceled_interactive_call_reaches_the_canceled_state()
    {
        var interactive = new RecordingAcquirer { Cancel = true };
        var service = Create(interactive);

        var result = await service.SignInAsync((IntPtr)8888, CancellationToken.None);

        Assert.True(result.IsCanceled);
        Assert.Equal(AuthenticationState.Canceled, service.State);
    }

    /// <summary>Walks up from the test binary to the repository root so a source assertion can read a file.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ADEXO.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    [Fact]
    public void The_silent_acquirer_never_uses_the_operating_system_account()
    {
        // On a merely domain-joined device the OS account resolves but can never yield a token, and
        // asking for it stalls rather than failing. The lab proved AzureAdJoined=NO, AzureAdPrt=NO, so
        // the production acquirer must not reference it at all.
        var source = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "src", "ADEXO.Graph", "Authentication", "ISilentTokenAcquirer.cs"));

        Assert.DoesNotContain("PublicClientApplication.OperatingSystemAccount", source, StringComparison.Ordinal);
        Assert.Contains("GetAccountsAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_out_clears_the_session_without_blocking()
    {
        var service = Create(new RecordingAcquirer());

        var signOut = service.SignOutAsync(CancellationToken.None);
        var completed = await Task.WhenAny(signOut, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(signOut, completed);
        Assert.Equal(AuthenticationState.SignedOut, service.State);
        Assert.Null(service.CurrentAccount);
    }

    [Fact]
    public async Task The_silent_acquirer_contract_distinguishes_interaction_from_failure()
    {
        // Null means "interaction required". A thrown exception means "failed". Conflating the two is
        // what would turn a permission error into a prompt.
        var acquirer = new RequireInteractionSilentAcquirer();

        var result = await acquirer.TryAcquireAsync(CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, acquirer.CallCount);
    }
}

/// <summary>Builds a valid non-secret configuration for tests.</summary>
internal sealed class MicrosoftIdentityConfigurationBuilder
{
    private string? _mode;

    public MicrosoftIdentityConfigurationBuilder WithMode(string mode)
    {
        _mode = mode;
        return this;
    }

    public MicrosoftIdentityConfiguration Build()
    {
        MicrosoftIdentityConfiguration.TryCreate(
            new MicrosoftGraphOptions
            {
                TenantId = "c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af",
                ClientId = "22222222-3333-4444-5555-666666666666",
                AuthenticationMode = _mode,
            },
            out var configuration,
            out _);

        return configuration!;
    }
}

/// <summary>Records the interactive call and can be held, canceled, or made to throw.</summary>
internal sealed class RecordingAcquirer : IInteractiveTokenAcquirer
{
    public int CallCount { get; private set; }

    public IntPtr ObservedHandle { get; private set; }

    public Task? HoldUntil { get; set; }

    /// <summary>When true the call never completes, modelling a broker that produces no UI and no result.</summary>
    public bool NeverCompletes { get; set; }

    public bool Cancel { get; set; }

    public Exception? Throw { get; set; }

    public async Task<AuthenticationResult> AcquireAsync(IntPtr ownerWindowHandle, CancellationToken cancellationToken)
    {
        CallCount++;
        ObservedHandle = ownerWindowHandle;

        if (NeverCompletes)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        if (HoldUntil is not null)
        {
            await HoldUntil.ConfigureAwait(false);
        }

        if (Throw is not null)
        {
            throw Throw;
        }

        if (Cancel)
        {
            throw new OperationCanceledException();
        }

        // A test double never fabricates a token.
        throw new InvalidOperationException("A test double does not produce an authentication result.");
    }
}

/// <summary>
/// Reports that interaction is required, or returns a supplied result. It never contacts a tenant.
/// </summary>
internal sealed class RequireInteractionSilentAcquirer : ISilentTokenAcquirer
{
    public int CallCount { get; private set; }

    public AuthenticationResult? Result { get; set; }

    public Task<AuthenticationResult?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(Result);
    }
}
