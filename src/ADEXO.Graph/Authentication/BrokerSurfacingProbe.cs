using System.Diagnostics;
using ADEXO.Contracts.Authentication;

namespace ADEXO.Graph.Authentication;

/// <summary>
/// Decides, and records, whether usable broker interaction surfaced in time.
/// <para>
/// The window exists to detect a broker that produces no interactive UI. It is explicitly not an
/// authentication deadline: once the UI has surfaced, or the broker reports that interaction is active,
/// the caller may take as long as the identity platform requires for account selection, credentials,
/// MFA, Conditional Access, or a password reset.
/// </para>
/// </summary>
public sealed class BrokerSurfacingProbe
{
    private readonly Stopwatch _elapsed = new();
    private readonly TimeSpan _window;
    private readonly IBrokerSurfaceDetector? _detector;
    private readonly TimeSpan _pollInterval;
    private readonly TaskCompletionSource _surfaced = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IntPtr _ownerWindowHandle;
    private int _uiSurfaced;

    /// <param name="window">
    /// How long the broker is observed for usable UI before the attempt is treated as having produced
    /// none. Defaults to the approved twenty seconds.
    /// </param>
    /// <param name="detector">
    /// Reports whether broker UI is on screen. Production supplies the window detector; a test may
    /// supply a stub, or none when it drives <see cref="MarkUiSurfaced"/> directly.
    /// </param>
    /// <param name="pollInterval">How often the detector is consulted while the window is open.</param>
    public BrokerSurfacingProbe(
        TimeSpan? window = null,
        IBrokerSurfaceDetector? detector = null,
        TimeSpan? pollInterval = null)
    {
        _window = window ?? MicrosoftIdentityConfiguration.BrokerSurfacingWindow;
        _detector = detector;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
    }

    /// <summary>True once the broker has shown its interactive UI or reported active interaction.</summary>
    public bool UiSurfaced => Volatile.Read(ref _uiSurfaced) != 0;

    public TimeSpan Elapsed => _elapsed.Elapsed;

    /// <summary>Starts observing. The handle is the parent window the broker was given.</summary>
    public void Start(IntPtr ownerWindowHandle = default)
    {
        _ownerWindowHandle = ownerWindowHandle;
        _elapsed.Restart();
    }

    public void Stop() => _elapsed.Stop();

    /// <summary>
    /// Called when the broker's interactive surface is observed, or when the broker reports that
    /// interaction is under way. After this the window no longer applies.
    /// </summary>
    public void MarkUiSurfaced()
    {
        if (Interlocked.Exchange(ref _uiSurfaced, 1) == 0)
        {
            _surfaced.TrySetResult();
        }
    }

    /// <summary>
    /// Classifies the outcome of a broker attempt that has finished without producing a token.
    /// </summary>
    /// <param name="activationFailed">True when MSAL reported a broker activation or runtime error.</param>
    public FallbackReason Classify(bool activationFailed)
    {
        if (activationFailed)
        {
            return FallbackReason.BrokerActivationFailure;
        }

        // UI was shown, so this is not a surfacing problem. The caller decides what to report.
        if (UiSurfaced)
        {
            return FallbackReason.None;
        }

        // The window elapsed with no UI: the broker never surfaced.
        return _elapsed.Elapsed >= _window
            ? FallbackReason.BrokerTimeout
            : FallbackReason.BrokerUiNotSurfaced;
    }

    /// <summary>
    /// Awaits the broker task, but stops observing it after the window if no usable UI has appeared.
    /// Returns null when the window expired without broker UI.
    /// <para>
    /// Once UI is observed the window stops applying entirely: the method then waits for the broker
    /// however long the operator needs.
    /// </para>
    /// </summary>
    public async Task<T?> AwaitOrExpireAsync<T>(Task<T> brokerTask, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(brokerTask);

        while (true)
        {
            if (UiSurfaced)
            {
                // Interaction is live. No deadline applies from here on; the operator may be entering
                // credentials, completing MFA, or satisfying a Conditional Access policy.
                return await brokerTask.ConfigureAwait(false);
            }

            var remaining = _window - _elapsed.Elapsed;

            if (remaining <= TimeSpan.Zero)
            {
                // One last look before giving up: the broker may have surfaced during the final step.
                if (_detector?.IsBrokerUiVisible(_ownerWindowHandle) == true)
                {
                    MarkUiSurfaced();
                    continue;
                }

                return null;
            }

            var step = remaining < _pollInterval ? remaining : _pollInterval;
            var delay = Task.Delay(step, cancellationToken);

            var first = await Task.WhenAny(brokerTask, _surfaced.Task, delay).ConfigureAwait(false);

            if (first == brokerTask)
            {
                return await brokerTask.ConfigureAwait(false);
            }

            if (first == _surfaced.Task)
            {
                // Surfacing is signalled rather than polled, so a broker that appears mid-step is not
                // missed because the step happened to end first.
                return await brokerTask.ConfigureAwait(false);
            }

            if (delay.IsCanceled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (_detector?.IsBrokerUiVisible(_ownerWindowHandle) == true)
            {
                MarkUiSurfaced();
            }
        }
    }
}
