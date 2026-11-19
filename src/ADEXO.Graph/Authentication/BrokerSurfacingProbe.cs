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
    private int _uiSurfaced;

    public BrokerSurfacingProbe(TimeSpan? window = null) =>
        _window = window ?? MicrosoftIdentityConfiguration.BrokerSurfacingWindow;

    /// <summary>True once the broker has shown its interactive UI or reported active interaction.</summary>
    public bool UiSurfaced => Volatile.Read(ref _uiSurfaced) != 0;

    public TimeSpan Elapsed => _elapsed.Elapsed;

    public void Start() => _elapsed.Restart();

    public void Stop() => _elapsed.Stop();

    /// <summary>
    /// Called when the broker's interactive surface is observed, or when the broker reports that
    /// interaction is under way. After this the window no longer applies.
    /// </summary>
    public void MarkUiSurfaced() => Interlocked.Exchange(ref _uiSurfaced, 1);

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
    /// Awaits the broker task, but gives up on observing it after the window if no UI has surfaced.
    /// Returns null when the window expired without broker UI.
    /// </summary>
    public async Task<T?> AwaitOrExpireAsync<T>(Task<T> brokerTask, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(brokerTask);

        var windowTask = Task.Delay(_window, cancellationToken);

        while (true)
        {
            var completed = await Task.WhenAny(brokerTask, windowTask).ConfigureAwait(false);

            if (completed == brokerTask)
            {
                return await brokerTask.ConfigureAwait(false);
            }

            // The window elapsed. If the UI surfaced meanwhile, the interaction is legitimate and the
            // caller must be allowed to finish, so keep waiting rather than abandoning it.
            if (UiSurfaced)
            {
                windowTask = Task.Delay(_window, cancellationToken);
                continue;
            }

            return null;
        }
    }
}
