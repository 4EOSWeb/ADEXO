using System.Diagnostics;
using System.Windows.Threading;

namespace ADEXO.Desktop;

/// <summary>
/// Reports progress when an interactive sign-in takes longer than expected.
/// <para>
/// Its purpose is visibility. It does not cancel, retry, or fall back to another authentication flow,
/// because the operator may legitimately be completing MFA. When the threshold passes it appends the
/// last completed checkpoint and the dispatcher's responsiveness to the status message, so a hang
/// produces evidence instead of silence.
/// </para>
/// </summary>
internal sealed class AuthenticationWatchdog : IDisposable
{
    /// <summary>How long an interactive sign-in may take before the first progress report.</summary>
    internal static readonly TimeSpan Threshold = TimeSpan.FromSeconds(30);

    private readonly CloudSectionViewModel _section;
    private readonly Stopwatch _elapsed = new();
    private readonly DispatcherTimer _timer;
    private readonly Func<int> _checkpointProbe;
    private bool _reported;

    public AuthenticationWatchdog(CloudSectionViewModel section, Func<int>? checkpointProbe = null)
    {
        _section = section;
        _checkpointProbe = checkpointProbe ?? (() => 0);

        // A dispatcher timer runs on the UI thread, so its tick itself proves the UI is still pumping.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Threshold };
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        _elapsed.Restart();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _elapsed.Stop();
    }

    public void Dispose()
    {
        _timer.Tick -= OnTick;
        Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_reported)
        {
            return;
        }

        _reported = true;

        // Reaching this handler means the dispatcher is alive; a sign-in still unresolved at this point
        // is waiting on something outside the UI thread.
        _section.ReportProgress(
            $"Sign-in is still in progress after {_elapsed.Elapsed.TotalSeconds:F0}s. "
            + $"The application is responsive and no dialog has completed. Checkpoints reached: {_checkpointProbe()}.");
    }
}
