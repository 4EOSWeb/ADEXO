using System.Runtime.InteropServices;

namespace ADEXO.Graph.Authentication;

/// <summary>
/// Verifies that a window handle is a real, live window belonging to this process before it is handed
/// to the interactive broker. A zero, stale, or foreign handle makes the broker fail in ways that do
/// not always surface as an exception.
/// </summary>
public interface IWindowHandleValidator
{
    /// <summary>Returns null when the handle is usable, otherwise a sanitized reason.</summary>
    string? Validate(IntPtr windowHandle);
}

/// <summary>
/// The production validator. It checks that the handle is nonzero, that Windows considers it a window,
/// and that it belongs to the current process.
/// </summary>
public sealed class WindowHandleValidator : IWindowHandleValidator
{
    public static WindowHandleValidator Instance { get; } = new();

    public string? Validate(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return "The interactive sign-in requires a window handle, but none was supplied.";
        }

        if (!IsWindow(windowHandle))
        {
            return "The supplied window handle is not a live window.";
        }

        _ = GetWindowThreadProcessId(windowHandle, out var owningProcessId);
        var currentProcessId = (uint)Environment.ProcessId;

        return owningProcessId != currentProcessId
            ? "The supplied window handle belongs to a different process."
            : null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
}

/// <summary>
/// One authentication checkpoint. It records stage, thread, apartment, and elapsed time only.
/// No token, credential, or identity value is ever recorded here.
/// </summary>
public sealed record AuthenticationCheckpoint(
    string Stage,
    int ManagedThreadId,
    string ApartmentState,
    long ElapsedMilliseconds)
{
    public override string ToString() =>
        $"{Stage} thread={ManagedThreadId} apartment={ApartmentState} elapsedMs={ElapsedMilliseconds}";
}
