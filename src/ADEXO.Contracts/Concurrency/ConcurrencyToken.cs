namespace ADEXO.Contracts.Concurrency;

public enum ConcurrencyTokenKind
{
    None,
    ActiveDirectorySnapshot,
    GraphETag,
    ExchangeSnapshot,
}

public sealed class ConcurrencyToken : IEquatable<ConcurrencyToken>
{
    private ConcurrencyToken(ConcurrencyTokenKind kind, string? value)
    {
        if (kind == ConcurrencyTokenKind.None && value is not null)
        {
            throw new ArgumentException("An unsupported token has no value.", nameof(value));
        }

        if (kind != ConcurrencyTokenKind.None && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A concurrency token needs a value.", nameof(value));
        }

        Kind = kind;
        Value = value;
    }

    public static ConcurrencyToken None { get; } = new(ConcurrencyTokenKind.None, null);

    public ConcurrencyTokenKind Kind { get; }

    public string? Value { get; }

    public static ConcurrencyToken From(ConcurrencyTokenKind kind, string value) => new(kind, value);

    public bool Equals(ConcurrencyToken? other) =>
        other is not null && Kind == other.Kind && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ConcurrencyToken);

    public override int GetHashCode() => HashCode.Combine(Kind, Value is null ? 0 : string.GetHashCode(Value, StringComparison.Ordinal));
}
