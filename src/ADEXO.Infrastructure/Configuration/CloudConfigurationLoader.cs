using System.Text.Json;
using ADEXO.Contracts.Configuration;

namespace ADEXO.Infrastructure.Configuration;

/// <summary>
/// Loads the non-secret cloud configuration from a local JSON file.
/// <para>
/// The file supplies only identifiers: the approved tenant and the application (client) identifier.
/// There is no password, secret, token, certificate, or administrator sign-in name here, and none may
/// be added: the loader refuses a document containing a property whose name suggests a secret, so a
/// contributor cannot quietly introduce one.
/// </para>
/// <para>
/// A missing file is an ordinary state, not a failure. A fresh installation has no configuration and
/// the cloud section simply reports that it is not configured.
/// </para>
/// </summary>
public static class CloudConfigurationLoader
{
    /// <summary>The default file name, resolved beside the executable.</summary>
    public const string DefaultFileName = "adexo.cloud.json";

    /// <summary>Names that may never appear as a property in the configuration document.</summary>
    private static readonly string[] ForbiddenPropertyNames =
    [
        "password", "secret", "token", "credential", "privatekey", "certificate",
        "clientsecret", "refreshtoken", "accesstoken", "keyvault", "thumbprint",
    ];

    /// <summary>
    /// Reads the configuration file. Returns null and a reason when the file is absent, unreadable,
    /// malformed, or contains a forbidden property.
    /// </summary>
    public static CloudConfigurationFile? TryLoad(string path, out string? reason)
    {
        reason = null;

        if (!File.Exists(path))
        {
            reason = $"No cloud configuration file was found at {Path.GetFileName(path)}.";
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reason = "The cloud configuration file could not be read.";
            return null;
        }

        return TryParse(text, out reason);
    }

    /// <summary>
    /// Parses the configuration document. Exposed so the rejection rules can be tested without a file.
    /// </summary>
    public static CloudConfigurationFile? TryParse(string json, out string? reason)
    {
        reason = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            reason = "The cloud configuration document is empty.";
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            reason = "The cloud configuration document is not valid JSON.";
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                reason = "The cloud configuration document must be a JSON object.";
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                // A secret-looking property is refused outright rather than ignored, so the mistake is
                // loud at load time instead of silently shipping a credential to disk.
                if (ForbiddenPropertyNames.Any(forbidden =>
                        property.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
                {
                    reason = $"The cloud configuration document contains a '{property.Name}' property, which is not permitted. Credentials never belong in configuration.";
                    return null;
                }
            }

            var tenantId = ReadString(document.RootElement, "tenantId");
            var clientId = ReadString(document.RootElement, "clientId");
            var authenticationMode = ReadString(document.RootElement, "authenticationMode");

            if (string.IsNullOrWhiteSpace(tenantId) && string.IsNullOrWhiteSpace(clientId))
            {
                reason = "The cloud configuration document does not supply a tenant or client identifier.";
                return null;
            }

            return new CloudConfigurationFile(tenantId, clientId, authenticationMode, sourcePath: null);
        }
    }

    /// <summary>
    /// Converts a parsed document into the options the identity layer consumes. Validation of the
    /// identifier shapes belongs to that layer, not here.
    /// </summary>
    public static MicrosoftGraphOptions ToOptions(this CloudConfigurationFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new MicrosoftGraphOptions
        {
            TenantId = file.TenantId,
            ClientId = file.ClientId,
            AuthenticationMode = file.AuthenticationMode ?? "Auto",
        };
    }

    private static string? ReadString(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }

        return null;
    }
}

/// <summary>
/// The parsed non-secret configuration document.
/// </summary>
/// <param name="TenantId">The approved tenant identifier.</param>
/// <param name="ClientId">The application (client) identifier of the public client.</param>
/// <param name="AuthenticationMode">The requested authentication mode, when supplied.</param>
/// <param name="sourcePath">Where the document was read from, when read from disk.</param>
public sealed record CloudConfigurationFile(string? TenantId, string? ClientId, string? AuthenticationMode, string? sourcePath)
{
    /// <summary>True when both identifiers are present and the cloud section can be offered.</summary>
    public bool IsComplete => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId);
}
