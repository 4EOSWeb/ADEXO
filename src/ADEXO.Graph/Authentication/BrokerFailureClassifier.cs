using Microsoft.Identity.Client;

namespace ADEXO.Graph.Authentication;

/// <summary>
/// Recognises the identity library failures that mean the Windows broker itself could not be used, as
/// opposed to the operator cancelling or the tenant refusing the sign-in.
/// <para>
/// The distinction matters because only a broker failure justifies the system-browser fallback. A
/// cancellation, a Conditional Access refusal, or a consent prompt is the operator's or the tenant's
/// answer and must be reported as such rather than retried down another path.
/// </para>
/// </summary>
public static class BrokerFailureClassifier
{
    /// <summary>
    /// Error codes the identity library raises when the broker is absent, cannot be activated, cannot
    /// host the request, or returns a broker-level error. These are the recognised broker failures.
    /// </summary>
    private static readonly string[] RecognizedBrokerErrorCodes =
    [
        "broker_required_for_pop",
        "broker_response_returned_error",
        "broker_application_required",
        "broker_signature_verification_failed",
        "wam_interactive_picker_error",
        "wam_no_b2c",
        "broker_not_installed",
        "broker_not_available",
    ];

    /// <summary>True when the exception is a broker-level failure rather than an operator outcome.</summary>
    public static bool IsBrokerFailure(Exception exception)
    {
        if (exception is not MsalException msal)
        {
            // A non-identity exception thrown while starting the broker is an activation failure: the
            // COM surface, the activation factory, or the app container could not be brought up.
            return exception is not OperationCanceledException;
        }

        if (string.IsNullOrEmpty(msal.ErrorCode))
        {
            return false;
        }

        return RecognizedBrokerErrorCodes.Any(code =>
            string.Equals(msal.ErrorCode, code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the operator dismissed the sign-in. Never a fallback trigger.</summary>
    public static bool IsUserCancellation(Exception exception) =>
        exception is MsalClientException msal
        && (string.Equals(msal.ErrorCode, "authentication_canceled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(msal.ErrorCode, "user_canceled", StringComparison.OrdinalIgnoreCase));
}
