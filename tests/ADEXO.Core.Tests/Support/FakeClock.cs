using ADEXO.Contracts.Time;

namespace ADEXO.Core.Tests.Support;

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow;

    public DateTimeOffset UtcNow { get; set; }
}
