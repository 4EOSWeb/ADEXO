namespace ADEXO.Contracts.Authority;

public enum SourceOfAuthority
{
    ActiveDirectory,
    MicrosoftEntra,
    ExchangeOnline,
    Calculated,
    Unsupported,
    Unknown,
}

public sealed class AuthoritativeValue<T>
{
    public AuthoritativeValue(
        T? value,
        SourceOfAuthority source,
        bool isEditable,
        string? reasonReadOnly,
        DateTimeOffset lastRefreshedUtc)
    {
        if (!isEditable && string.IsNullOrWhiteSpace(reasonReadOnly))
        {
            throw new ArgumentException("A read-only value must explain why.", nameof(reasonReadOnly));
        }

        Value = value;
        Source = source;
        IsEditable = isEditable;
        ReasonReadOnly = reasonReadOnly;
        LastRefreshedUtc = lastRefreshedUtc;
    }

    public T? Value { get; }

    public SourceOfAuthority Source { get; }

    public bool IsEditable { get; }

    public string? ReasonReadOnly { get; }

    public DateTimeOffset LastRefreshedUtc { get; }
}
