using ADEXO.Contracts.Authentication;
using ADEXO.Graph.Authentication;

namespace ADEXO.Graph.Tests;

/// <summary>
/// The bounded broker-surfacing probe. These tests pin the distinction the design depends on: the window
/// detects a broker that shows no UI, and never acts as an authentication deadline.
/// </summary>
public sealed class BrokerSurfacingProbeTests
{
    [Fact]
    public void An_unstarted_probe_reports_that_no_ui_has_surfaced()
    {
        var probe = new BrokerSurfacingProbe();

        Assert.False(probe.UiSurfaced);
        // Nothing has surfaced and no window has elapsed, so the broker has shown no UI.
        Assert.Equal(FallbackReason.BrokerUiNotSurfaced, probe.Classify(activationFailed: false));
    }

    [Fact]
    public void Marking_ui_surfaced_means_a_missing_token_is_not_a_surfacing_failure()
    {
        var probe = new BrokerSurfacingProbe();

        probe.MarkUiSurfaced();

        // Once UI surfaced, the caller's outcome (cancel, failure) is theirs, not a fallback trigger.
        Assert.True(probe.UiSurfaced);
        Assert.Equal(FallbackReason.None, probe.Classify(activationFailed: false));
    }

    [Fact]
    public void An_activation_failure_is_classified_as_activation()
    {
        var probe = new BrokerSurfacingProbe();

        Assert.Equal(FallbackReason.BrokerActivationFailure, probe.Classify(activationFailed: true));
    }

    [Fact]
    public async Task Expiring_without_ui_returns_null_rather_than_waiting_forever()
    {
        // A broker that never surfaces and never completes must not hang the caller.
        var probe = new BrokerSurfacingProbe(TimeSpan.FromMilliseconds(120), pollInterval: TimeSpan.FromMilliseconds(20));
        probe.Start();
        var never = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var result = await probe.AwaitOrExpireAsync(never.Task, CancellationToken.None);

        Assert.Null(result);
        Assert.False(probe.UiSurfaced);
    }

    [Fact]
    public async Task Once_ui_surfaces_the_window_no_longer_expires_the_attempt()
    {
        // This is the requirement that a slow operator is never treated as a failure: account selection,
        // credentials, MFA, Conditional Access, or a password reset may take far longer than the window.
        // The completion deliberately arrives well after the window has elapsed.
        var probe = new BrokerSurfacingProbe(TimeSpan.FromMilliseconds(200), pollInterval: TimeSpan.FromMilliseconds(20));
        probe.Start();

        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            await Task.Delay(20);
            probe.MarkUiSurfaced();
            await Task.Delay(400);
            completion.SetResult("done");
        });

        var result = await probe.AwaitOrExpireAsync(completion.Task, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(probe.UiSurfaced);
    }

    [Fact]
    public async Task A_broker_that_surfaces_no_ui_within_the_window_is_reported_as_not_surfaced()
    {
        // The window is a failure-to-surface detector. A broker that never shows UI must not keep the
        // caller waiting, and the classification must say what actually happened.
        var probe = new BrokerSurfacingProbe(TimeSpan.FromMilliseconds(120), pollInterval: TimeSpan.FromMilliseconds(20));
        probe.Start();

        var never = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var result = await probe.AwaitOrExpireAsync(never.Task, CancellationToken.None);

        Assert.Null(result);
        Assert.False(probe.UiSurfaced);
        Assert.Equal(FallbackReason.BrokerTimeout, probe.Classify(activationFailed: false));
    }

    [Fact]
    public async Task A_completed_broker_task_is_returned_promptly()
    {
        var probe = new BrokerSurfacingProbe(TimeSpan.FromSeconds(5));
        probe.Start();

        var result = await probe.AwaitOrExpireAsync(Task.FromResult("value"), CancellationToken.None);

        Assert.Equal("value", result);
    }

    [Fact]
    public void The_default_window_is_twenty_seconds()
    {
        // The approved threshold. A change to this number is a design decision, not a tweak.
        Assert.Equal(TimeSpan.FromSeconds(20), MicrosoftIdentityConfiguration.BrokerSurfacingWindow);
    }
}
