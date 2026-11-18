using System.Text.Json;
using System.Text.Json.Serialization;

namespace ADEXO.Contracts.Secrets;

/// <summary>
/// Explicit wrapper for a secret. This is not a scanner for secrets hidden in arbitrary text.
/// </summary>
[JsonConverter(typeof(SecretTextJsonConverter))]
public sealed class SecretText
{
    public const string RedactedMarker = "[REDACTED]";

    private readonly string _value;

    private SecretText(string value) => _value = value;

    public static SecretText From(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return new SecretText(value);
    }

    internal static SecretText RedactedPlaceholder() => new(string.Empty);

    public override string ToString() => RedactedMarker;
}

public sealed class SecretTextJsonConverter : JsonConverter<SecretText>
{
    public override SecretText? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("A secret cannot be read from JSON.");
        }

        var text = reader.GetString();
        if (text == SecretText.RedactedMarker)
        {
            return SecretText.RedactedPlaceholder();
        }

        throw new JsonException("Refusing to deserialize a secret value.");
    }

    public override void Write(Utf8JsonWriter writer, SecretText value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(SecretText.RedactedMarker);
    }
}
