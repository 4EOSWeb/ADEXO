using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Configuration;

namespace ADEXO.Graph.Authentication;

/// <summary>
/// The non-secret settings the public client needs. Only an identifier pair is required: there is no
/// secret, certificate, or administrator sign-in name here, by design.
/// </summary>
public sealed class MicrosoftIdentityConfiguration
{
    private MicrosoftIdentityConfiguration(Guid tenantId, string clientId)
    {
        TenantId = tenantId;
        ClientId = clientId;
    }

    public Guid TenantId { get; }

    public string ClientId { get; }

    /// <summary>The delegated scopes Phase 3A requests. Read-only, and no application permission.</summary>
    public static IReadOnlyList<string> Scopes { get; } =
    [
        "User.Read",
        "User.Read.All",
        "Organization.Read.All",
    ];

    /// <summary>
    /// Builds the configuration, or reports that it is absent. A missing or malformed identifier is an
    /// ordinary "not configured" state rather than an exception, because a fresh installation has none.
    /// </summary>
    public static bool TryCreate(MicrosoftGraphOptions? options, out MicrosoftIdentityConfiguration? configuration, out string? reason)
    {
        configuration = null;
        reason = null;

        if (options is null || string.IsNullOrWhiteSpace(options.TenantId) || string.IsNullOrWhiteSpace(options.ClientId))
        {
            reason = "No tenant or client identifier is configured.";
            return false;
        }

        if (!Guid.TryParse(options.TenantId.Trim(), out var tenantId) || tenantId == Guid.Empty)
        {
            reason = "The configured tenant identifier is not a valid directory identifier.";
            return false;
        }

        var clientId = options.ClientId.Trim();
        if (!Guid.TryParse(clientId, out _))
        {
            reason = "The configured client identifier is not a valid application identifier.";
            return false;
        }

        configuration = new MicrosoftIdentityConfiguration(tenantId, clientId);
        return true;
    }

    /// <summary>The single-tenant authority. A multi-tenant authority would widen the sign-in surface.</summary>
    public string Authority => $"https://login.microsoftonline.com/{TenantId:D}";

    /// <summary>
    /// The broker redirect the app registration must carry, per Microsoft's WAM guidance: "You don't
    /// need to configure WAM redirect URIs in MSAL, but you do need to configure them in the app
    /// registration". This value is for the registration only and is deliberately not passed to
    /// <c>WithRedirectUri</c>, because MSAL resolves it from the broker.
    /// </summary>
    public string RegistrationRedirectUri => $"ms-appx-web://Microsoft.AAD.BrokerPlugin/{ClientId}";
}
