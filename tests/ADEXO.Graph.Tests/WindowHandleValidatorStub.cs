using ADEXO.Graph.Authentication;

namespace ADEXO.Graph.Tests;

/// <summary>
/// A window handle validator for tests. The production validator inspects real Win32 windows; these
/// assertions are about what the service does with the verdict, not about Win32 behavior.
/// </summary>
internal sealed class StubWindowHandleValidator : IWindowHandleValidator
{
    /// <summary>Null means the handle is acceptable.</summary>
    public string? Reason { get; set; }

    public IntPtr LastValidated { get; private set; }

    public int CallCount { get; private set; }

    public string? Validate(IntPtr windowHandle)
    {
        CallCount++;
        LastValidated = windowHandle;
        return Reason;
    }
}
