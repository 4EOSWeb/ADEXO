using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ADEXO.Graph.Authentication;

/// <summary>
/// Reports whether the Windows broker has actually put interactive UI on screen for a given parent window.
/// <para>
/// This signal is what keeps the twenty-second window from behaving like an authentication deadline. The
/// window measures only "no usable broker UI yet"; once this reports that UI exists, the window stops
/// applying and the operator may take as long as MFA, Conditional Access, or a password reset requires.
/// </para>
/// </summary>
public interface IBrokerSurfaceDetector
{
    /// <summary>
    /// True when broker interactive UI is observable for this parent window. Best effort by nature: it
    /// looks for a visible window the broker created, either owned by the parent window or owned by one
    /// of the processes the account manager uses to host its surface.
    /// </summary>
    bool IsBrokerUiVisible(IntPtr ownerWindowHandle);
}

/// <summary>
/// The production detector.
/// <para>
/// It deliberately prefers the strong signal: the broker parents its dialog to the window supplied as the
/// parent activity, so any visible top-level window owned by that handle is broker UI. That is specific
/// enough to avoid mistaking an unrelated application's window for a sign-in prompt, which would
/// suppress the fallback and leave the operator with a section that never resolves.
/// </para>
/// <para>
/// A missing signal is treated as "not surfaced", which is the conservative reading for the purpose it
/// serves: the fallback exists because a broker can be present and healthy yet still produce no usable
/// UI. The broker attempt is retired before the browser starts, so a broker that surfaces late can never
/// apply a result over a newer browser session.
/// </para>
/// </summary>
public sealed class BrokerWindowDetector : IBrokerSurfaceDetector
{
    /// <summary>
    /// Processes that carry the account manager's own interactive surface. Named narrowly on purpose:
    /// a generic shell host also owns windows for unrelated applications, so including it would report
    /// UI that is not a sign-in prompt.
    /// </summary>
    private static readonly string[] BrokerHostProcessNames =
    [
        "Microsoft.AAD.BrokerPlugin",
    ];

    public static BrokerWindowDetector Instance { get; } = new();

    public bool IsBrokerUiVisible(IntPtr ownerWindowHandle)
    {
        try
        {
            var currentProcessId = (uint)Environment.ProcessId;

            foreach (var (processId, handle) in EnumerateVisibleWindows())
            {
                // An owned window counts only when it belongs to another process. ADEXO's own dialogs and
                // popups are also owned by the main window, and counting them would report broker UI that
                // does not exist, which suppresses the fallback and leaves the section waiting forever.
                if (ownerWindowHandle != IntPtr.Zero
                    && processId != currentProcessId
                    && GetWindow(handle, GwOwner) == ownerWindowHandle)
                {
                    return true;
                }

                if (IsBrokerHost(processId))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A detection failure must not present itself as "UI is visible": that would suppress the
            // fallback entirely.
            return false;
        }
    }

    private static bool IsBrokerHost(uint processId)
    {
        Process process;
        try
        {
            process = Process.GetProcessById((int)processId);
        }
        catch (ArgumentException)
        {
            // The window's process exited between enumeration and inspection.
            return false;
        }

        using (process)
        {
            try
            {
                return BrokerHostProcessNames.Any(name =>
                    string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase));
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    private static IEnumerable<(uint ProcessId, nint Handle)> EnumerateVisibleWindows()
    {
        var found = new List<(uint, nint)>();

        _ = EnumWindows(
            (handle, _) =>
            {
                if (!IsWindowVisible(handle))
                {
                    return true;
                }

                // A zero-length title is a container, not something an administrator interacts with.
                if (GetWindowTextLength(handle) <= 0)
                {
                    return true;
                }

                var processId = GetWindowThreadProcessId(handle, out var ownerProcessId);
                if (processId != 0)
                {
                    found.Add((ownerProcessId, handle));
                }

                return true;
            },
            nint.Zero);

        return found;
    }

    private const uint GwOwner = 4;

    private delegate bool EnumWindowsCallback(nint handle, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetWindow(nint handle, uint command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
}
