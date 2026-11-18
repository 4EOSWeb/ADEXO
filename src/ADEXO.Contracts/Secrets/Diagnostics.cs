using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ADEXO.Contracts.Secrets;

public static class DiagnosticSanitizer
{
    private static readonly Regex Bearer = new(@"Bearer\s+\S+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var builder = new StringBuilder();
        Append(builder, exception);
        return builder.ToString();
    }

    public static string Render(object? value) => value switch
    {
        null => string.Empty,
        SecretText => SecretText.RedactedMarker,
        _ => ApplyPatternPass(value.ToString() ?? string.Empty),
    };

    /// <summary>
    /// Extra diagnostic pass for recognizable bearer tokens. Passwords and client secrets are not identified this way.
    /// </summary>
    public static string ApplyPatternPass(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Bearer.Replace(text, "Bearer [REDACTED]");
    }

    private static void Append(StringBuilder builder, Exception exception)
    {
        builder.Append(exception.GetType().Name);
        builder.Append(": ");
        builder.Append(Render(exception.Message));
        foreach (DictionaryEntry entry in exception.Data)
        {
            builder.Append(' ');
            builder.Append(entry.Key);
            builder.Append('=');
            builder.Append(Render(entry.Value));
        }

        if (exception.InnerException is not null)
        {
            builder.Append(" | ");
            Append(builder, exception.InnerException);
        }
    }
}

public sealed class SecretSafeLogState : IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly KeyValuePair<string, object?>[] _fields;

    public SecretSafeLogState(IEnumerable<KeyValuePair<string, object?>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _fields = fields.Select(field => new KeyValuePair<string, object?>(field.Key, Redact(field.Value))).ToArray();
    }

    public int Count => _fields.Length;

    public KeyValuePair<string, object?> this[int index] => _fields[index];

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)_fields).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => string.Join(", ", _fields.Select(field => $"{field.Key}={field.Value}"));

    private static object? Redact(object? value) => value is SecretText ? SecretText.RedactedMarker : value;
}

public static class SecretSafeLog
{
    public static void Information(ILogger logger, string message, params KeyValuePair<string, object?>[] fields)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var state = new SecretSafeLogState(fields);
        logger.Log(LogLevel.Information, 0, state, null, static (value, _) => value.ToString() ?? string.Empty);
    }
}
