using ADEXO.Contracts.Authentication;
using ADEXO.Graph.Authentication;

namespace ADEXO.Graph.Tests;

/// <summary>
/// The broker-failure classifier decides which failures may fall back to the browser.
/// <para>
/// Getting this wrong is a security or usability defect in both directions: falling back on a tenant's
/// Conditional Access refusal hides the real policy failure and cannot resolve it, while refusing to fall
/// back on a broker that cannot be activated leaves the operator with a dead sign-in.
/// </para>
/// </summary>
public sealed class BrokerFailureClassifierTests
{
    [Theory]
    [InlineData("broker_response_returned_error")]
    [InlineData("broker_application_required")]
    [InlineData("broker_not_available")]
    [InlineData("broker_not_installed")]
    [InlineData("wam_interactive_picker_error")]
    public void Recognized_broker_codes_are_treated_as_broker_failures(string errorCode)
    {
        var exception = new Microsoft.Identity.Client.MsalClientException(errorCode, "synthetic");

        Assert.True(BrokerFailureClassifier.IsBrokerFailure(exception));
        Assert.False(BrokerFailureClassifier.IsUserCancellation(exception));
    }

    [Theory]
    [InlineData("authentication_canceled")]
    [InlineData("user_canceled")]
    [InlineData("AUTHENTICATION_CANCELED")]
    public void An_operator_cancellation_is_recognised_and_is_not_a_broker_failure(string errorCode)
    {
        var exception = new Microsoft.Identity.Client.MsalClientException(errorCode, "synthetic");

        Assert.True(BrokerFailureClassifier.IsUserCancellation(exception));
        Assert.False(BrokerFailureClassifier.IsBrokerFailure(exception));
    }

    [Fact]
    public void A_tenant_policy_answer_is_neither_a_cancellation_nor_a_broker_failure()
    {
        // Conditional Access, consent, and licence refusals carry a service error code and must be
        // reported as themselves rather than retried down another path.
        var exception = new Microsoft.Identity.Client.MsalServiceException(
            "AADSTS53003",
            "Access has been blocked by Conditional Access policies.");

        Assert.False(BrokerFailureClassifier.IsUserCancellation(exception));
        Assert.False(BrokerFailureClassifier.IsBrokerFailure(exception));
    }

    [Fact]
    public void A_non_identity_exception_while_starting_the_broker_is_an_activation_failure()
    {
        // The COM surface or app container failing to come up does not always arrive as an MSAL error.
        Assert.True(BrokerFailureClassifier.IsBrokerFailure(new System.Runtime.InteropServices.COMException("activation")));
        Assert.True(BrokerFailureClassifier.IsBrokerFailure(new InvalidOperationException("activation")));
    }

    [Fact]
    public void A_cancellation_exception_is_not_treated_as_a_broker_failure()
    {
        Assert.False(BrokerFailureClassifier.IsBrokerFailure(new OperationCanceledException()));
    }
}

/// <summary>
/// The surfacing probe must never treat elapsed time alone as proof that the operator was too slow. These
/// tests pin the two halves of the approved rule: a missing window is actionable, a live interaction is not.
/// </summary>
public sealed class BrokerSurfacingWindowTests
{
    [Fact]
    public async Task A_detector_reporting_ui_keeps_the_attempt_open_indefinitely()
    {
        // The operator is completing MFA well past the window. The probe must keep waiting.
        var probe = new BrokerSurfacingProbe(
            TimeSpan.FromMilliseconds(120),
            new AlwaysVisibleDetector(),
            pollInterval: TimeSpan.FromMilliseconds(10));
        probe.Start((IntPtr)1234);

        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            await Task.Delay(400);
            completion.SetResult("authenticated");
        });

        var result = await probe.AwaitOrExpireAsync(completion.Task, CancellationToken.None);

        Assert.Equal("authenticated", result);
        Assert.True(probe.UiSurfaced);
    }

    [Fact]
    public void Marking_ui_surfaced_before_the_window_expires_reports_no_fallback_reason()
    {
        var probe = new BrokerSurfacingProbe(TimeSpan.FromSeconds(1));
        probe.Start();

        probe.MarkUiSurfaced();

        Assert.Equal(FallbackReason.None, probe.Classify(activationFailed: false));
    }

    [Fact]
    public void An_activation_failure_outranks_a_surfaced_window()
    {
        // The broker showed UI but then failed to activate further. The activation failure is the more
        // specific and more useful diagnosis.
        var probe = new BrokerSurfacingProbe(TimeSpan.FromSeconds(1));
        probe.Start();
        probe.MarkUiSurfaced();

        Assert.Equal(FallbackReason.BrokerActivationFailure, probe.Classify(activationFailed: true));
    }

    private sealed class AlwaysVisibleDetector : IBrokerSurfaceDetector
    {
        public bool IsBrokerUiVisible(IntPtr ownerWindowHandle) => true;
    }
}
