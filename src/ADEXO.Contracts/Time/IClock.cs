namespace ADEXO.Contracts.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
