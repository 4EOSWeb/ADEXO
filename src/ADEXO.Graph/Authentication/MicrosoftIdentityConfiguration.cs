using ADEXO.Contracts.Authentication;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Configuration;

namespace ADEXO.Graph.Authentication;

/// <summary>
/// The non-secret settings the public client needs. Only an identifier pair is required: there is no
/// secret, certificate, or administrator sign-in name here, by design.
/// </summary>
public sealed class MicrosoftIdentityConfiguration
{
    private MicrosoftIdentityConfiguration(Guid tenantId, string clientId, AuthenticationMode mode = AuthenticationMode.Auto)
    {
        TenantId = tenantId;
        ClientId = clientId;
        Mode = mode;
    }

    public Guid TenantId { get; }

    public string ClientId { get; }

    /// <summary>
    /// The configured authentication mode. An unrecognized value is treated as Auto rather than failing,
    /// because a typo in a mode name should not prevent an administrator from signing in.
    /// </summary>
    public AuthenticationMode Mode { get; private set; } = AuthenticationMode.Auto;

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

        var mode = ParseMode(options.AuthenticationMode);
        configuration = new MicrosoftIdentityConfiguration(tenantId, clientId, mode);
        return true;
    }

    /// <summary>
    /// Reads the mode. An absent or unrecognized value falls back to Auto: a typo should not lock an
    /// administrator out, and Auto is both the default and the safest preference order.
    /// </summary>
    private static AuthenticationMode ParseMode(string? value) =>
        Enum.TryParse<AuthenticationMode>(value?.Trim(), ignoreCase: true, out var mode)
            ? mode
            : AuthenticationMode.Auto;

    /// <summary>The single-tenant authority. A multi-tenant authority would widen the sign-in surface.</summary>
    public string Authority => $"https://login.microsoftonline.com/{TenantId:D}";

    /// <summary>
    /// The broker redirect the app registration must carry, per Microsoft's WAM guidance: "You don't
    /// need to configure WAM redirect URIs in MSAL, but you do need to configure them in the app
    /// registration". This value is for the registration only and is deliberately not passed to
    /// <c>WithRedirectUri</c>, because MSAL resolves it from the broker.
    /// </summary>
    public string RegistrationRedirectUri => $"ms-appx-web://Microsoft.AAD.BrokerPlugin/{ClientId}";

    /// <summary>
    /// The loopback redirect the app registration must carry for the system-browser path. MSAL listens on
    /// a loopback port itself and manages the authorization code and PKCE exchange; this value is the
    /// registration entry only.
    /// </summary>
    public const string BrowserRedirectRegistration = "http://localhost";

    /// <summary>
    /// How long the broker is observed for usable interactive UI before the browser fallback is used.
    /// <para>
    /// This is a failure-to-surface detector, not an authentication deadline. Once the broker UI has
    /// surfaced, or broker interaction is demonstrably active, the window no longer applies: an
    /// administrator may take as long as they need for account selection, credentials, MFA,
    /// Conditional Access, or a password reset.
    /// </para>
    /// </summary>
    public static TimeSpan BrokerSurfacingWindow { get; } = TimeSpan.FromSeconds(20);
}
