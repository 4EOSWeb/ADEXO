using ADEXO.Contracts.Launch;
using ADEXO.Contracts.Time;

namespace ADEXO.Core.Launch;

public interface IAducLaunchRequestParser
{
    LaunchParseOutcome Parse(IReadOnlyList<string> arguments, LaunchSource source);
}

public sealed class AducLaunchRequestParser : IAducLaunchRequestParser
{
    public const int MaximumArgumentLength = 2048;

    private readonly IClock _clock;

    public AducLaunchRequestParser(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    public LaunchParseOutcome Parse(IReadOnlyList<string> arguments, LaunchSource source)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
        {
            return LaunchParseOutcome.Empty();
        }

        if (arguments.Count != 2)
        {
            return LaunchParseOutcome.Failure("argument-count", "ADUC launch accepts the LDAP path and the object class.");
        }

        var path = arguments[0];
        var objectClass = arguments[1];
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(objectClass))
        {
            return LaunchParseOutcome.Failure("missing-value", "The LDAP path and object class are required.");
        }

        if (path.Length > MaximumArgumentLength || objectClass.Length > MaximumArgumentLength)
        {
            return LaunchParseOutcome.Failure("length", "An argument exceeds the supported length.");
        }

        if (ContainsControl(path) || ContainsControl(objectClass))
        {
            return LaunchParseOutcome.Failure("control-character", "An argument contains a prohibited control character.");
        }

        if (!objectClass.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            return LaunchParseOutcome.Failure("unsupported-class", "Phase 2 can open a user object only.");
        }

        if (HasUnsupportedScheme(path))
        {
            return LaunchParseOutcome.Failure("unsupported-scheme", "The path uses a scheme that ADEXO does not open.");
        }

        if (!LdapUrlParser.TryParse(path, out var parsed, out var error) || parsed is null)
        {
            return LaunchParseOutcome.Failure("malformed-ldap", error ?? "The LDAP path is malformed.");
        }

        return LaunchParseOutcome.Success(new AducLaunchRequest(parsed, "user", source, _clock.UtcNow));
    }

    private static bool ContainsControl(string value) => value.Any(char.IsControl);

    private static bool HasUnsupportedScheme(string value)
    {
        var schemes = new[] { "file:", "http:", "https:", "powershell:", "cmd:", "shell:", "javascript:", "data:", "ldaps:", "gc:", "winnt:" };
        return schemes.Any(scheme => value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase));
    }
}
