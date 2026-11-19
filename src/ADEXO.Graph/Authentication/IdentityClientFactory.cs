using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

namespace ADEXO.Graph.Authentication;

/// <summary>Which MSAL client shape to build.</summary>
public enum IdentityClientKind
{
    /// <summary>Public client with the Windows broker. No browser redirect is configured.</summary>
    WindowsBroker,

    /// <summary>Public client using the system browser and MSAL's loopback redirect.</summary>
    SystemBrowser,
}

/// <summary>
/// Builds the two intentional MSAL client configurations.
/// <para>
/// They are deliberately separate rather than one client with a switch, because the two paths differ in
/// a way that matters: the broker variant must not carry a browser redirect, and the browser variant must
/// not configure a broker. Mixing them produces a client that silently behaves like neither.
/// </para>
/// </summary>
public static class IdentityClientFactory
{
    /// <summary>
    /// Builds the client for the requested kind. Both use the same client identifier, the same
    /// single-tenant authority, and the same read-only delegated scopes.
    /// </summary>
    public static IPublicClientApplication Create(MicrosoftIdentityConfiguration configuration, IdentityClientKind kind)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = PublicClientApplicationBuilder
            .Create(configuration.ClientId)
            .WithAuthority(configuration.Authority, validateAuthority: true)
            .WithClientName("ADEXO");

        return kind switch
        {
            IdentityClientKind.WindowsBroker => builder
                .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { MsaPassthrough = false })
                .Build(),

            IdentityClientKind.SystemBrowser => builder
                .WithRedirectUri(MicrosoftIdentityConfiguration.BrowserRedirectRegistration)
                .Build(),

            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}
