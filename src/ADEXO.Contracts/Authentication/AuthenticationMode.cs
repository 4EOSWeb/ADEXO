namespace ADEXO.Contracts.Authentication;

/// <summary>
/// Which interactive authentication path the application may use.
/// </summary>
public enum AuthenticationMode
{
    /// <summary>
    /// Prefer the Windows broker, and fall back to the system browser when usable broker interaction does
    /// not surface. This is the default.
    /// </summary>
    Auto,

    /// <summary>
    /// The Windows broker only. The system browser is never launched, even on failure.
    /// </summary>
    WamOnly,

    /// <summary>
    /// The system browser only. The Windows broker is skipped entirely.
    /// </summary>
    SystemBrowserOnly,
}

/// <summary>Which mechanism actually performed the interactive authentication.</summary>
public enum AuthenticationMethod
{
    None,
    WindowsBroker,
    SystemBrowser,
}

/// <summary>
/// Why the fallback from the broker to the system browser occurred. Strongly typed so diagnostics and
/// tests can distinguish an unavailable broker from one that simply never showed UI.
/// </summary>
public enum FallbackReason
{
    /// <summary>No fallback occurred.</summary>
    None,

    /// <summary>The broker package is not present or could not be loaded.</summary>
    BrokerUnavailable,

    /// <summary>The broker was present but activation failed with an error.</summary>
    BrokerActivationFailure,

    /// <summary>The broker started but never produced a usable interactive surface.</summary>
    BrokerUiNotSurfaced,

    /// <summary>Usable broker interaction did not begin within the bounded observation window.</summary>
    BrokerTimeout,

    /// <summary>An administrator explicitly chose the browser path.</summary>
    ExplicitUserFallback,
}

/// <summary>
/// What happened during one interactive sign-in attempt, for diagnostics. Contains no token material.
/// </summary>
public sealed class AuthenticationAttemptReport
{
    /// <summary>The attempt number, so a stale completion can be recognised.</summary>
    public int AttemptId { get; init; }

    public AuthenticationMode Mode { get; init; }

    /// <summary>The mechanism that produced the result.</summary>
    public AuthenticationMethod Method { get; init; }

    public FallbackReason FallbackReason { get; init; }

    /// <summary>
    /// How long the broker was observed before the fallback decision. Null when no broker attempt was made.
    /// </summary>
    public TimeSpan? BrokerStartElapsed { get; init; }

    /// <summary>True when the broker's interactive UI was observed to surface.</summary>
    public bool BrokerUiSurfaced { get; init; }

    /// <summary>Whether the attempt was superseded by a newer attempt.</summary>
    public bool Superseded { get; init; }
}
