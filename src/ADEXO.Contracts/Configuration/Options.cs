namespace ADEXO.Contracts.Configuration;

public sealed class ApplicationOptions
{
    public string ProductName { get; init; } = "ADEXO";
}

public sealed class ActiveDirectoryOptions
{
    public string? Domain { get; init; }
}

public sealed class MicrosoftGraphOptions
{
    public string? TenantId { get; init; }

    /// <summary>Application (client) identifier of the ADEXO public client registration. Not a secret.</summary>
    public string? ClientId { get; init; }
}

public sealed class ExchangeOnlineOptions
{
    public string? Organization { get; init; }
}

public sealed class EntraConnectOptions
{
    public bool Enabled { get; init; }

    public string? Server { get; init; }
}

public sealed class DiagnosticsOptions
{
    public bool IncludeTechnicalDetails { get; init; }
}

public static class ConfigurationGuard
{
    private static readonly string[] ForbiddenNameParts = ["Password", "Secret", "PrivateKey", "RefreshToken", "AccessToken"];

    public static void EnsureNoSecretProperties(params Type[] optionTypes)
    {
        foreach (var type in optionTypes)
        {
            foreach (var property in type.GetProperties())
            {
                if (ForbiddenNameParts.Any(part => property.Name.Contains(part, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"Configuration type {type.Name} exposes {property.Name}.");
                }
            }
        }
    }
}
