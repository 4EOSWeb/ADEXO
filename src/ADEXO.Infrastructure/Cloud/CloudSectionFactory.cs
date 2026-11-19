using ADEXO.Contracts.Authentication;
using ADEXO.Contracts.Configuration;
using ADEXO.Contracts.Time;
using ADEXO.ExchangeOnline;
using ADEXO.Graph;
using ADEXO.Graph.Authentication;
using ADEXO.Graph.Orchestration;
using ADEXO.Graph.Reads;
using ADEXO.Infrastructure.Configuration;
using Microsoft.Graph;
using Microsoft.Identity.Client;

namespace ADEXO.Infrastructure.Cloud;

/// <summary>
/// The cloud components assembled from configuration. The desktop shell composes its own view model
/// from these; this assembly must not depend on the shell.
/// </summary>
/// <param name="Authentication">The interactive authentication service.</param>
/// <param name="Session">The ordered tenant-lock and correlation pipeline.</param>
/// <param name="TenantId">The approved tenant the session locks to.</param>
/// <param name="Mailboxes">The read-only Exchange mailbox provider, when Exchange is composed.</param>
public sealed record CloudComponents(
    IMicrosoftAuthenticationService Authentication,
    CloudReadSession Session,
    Guid TenantId,
    Contracts.Mailbox.IMailboxProvider? Mailboxes = null);

/// <summary>
/// Composes the Microsoft 365 pieces from non-secret configuration.
/// <para>
/// When the configuration file is absent or invalid, this returns null components and a reason rather
/// than failing. A fresh installation, or a workstation with no lab tenant, must still run and still
/// show Active Directory data.
/// </para>
/// </summary>
public static class CloudSectionFactory
{
    /// <summary>
    /// Builds the components, or reports why they could not be built. Only identifiers are read; no
    /// password, secret, token, or certificate is accepted from configuration.
    /// </summary>
    public static (CloudComponents? Components, string? Reason) Create(string configurationPath, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var file = CloudConfigurationLoader.TryLoad(configurationPath, out var loadReason);
        if (file is null)
        {
            return (null, loadReason);
        }

        var options = file.ToOptions();
        if (!MicrosoftIdentityConfiguration.TryCreate(options, out var configuration, out var configReason)
            || configuration is null)
        {
            return (null, configReason);
        }

        // The service owns application construction so the broker configuration cannot drift between
        // two call sites. Passing null makes it build the public client with the Windows broker.
        var authentication = new PublicClientAuthenticationService(configuration);

        // The token is attached per request by the adapter's access-token provider, so it is never
        // held outside the pipeline that consumes it.
        var graphClient = new GraphServiceClient(BuildRequestAdapter(authentication));

        var provider = new GraphCloudProvider(new GraphReadClient(graphClient), clock);
        var session = new CloudReadSession(authentication, provider, configuration.TenantId);

        // Exchange is composed but not connected. Connection is an explicit administrator action, so the
        // provider exists without a live session and reports itself as not connected until one is made.
        var exchangeRunspace = ExchangeRunspace.Create();
        var mailboxes = new ExchangeMailboxProvider(new ExchangeRunspaceInvoker(exchangeRunspace), clock);

        return (new CloudComponents(authentication, session, configuration.TenantId, mailboxes), null);
    }
    /// <summary>
    /// Builds the Graph request adapter from the public client. The auth provider is ours, so the token
    /// path is the MSAL public client and not a credential chain.
    /// </summary>
    private static Microsoft.Kiota.Abstractions.IRequestAdapter BuildRequestAdapter(
        PublicClientAuthenticationService authentication) =>
        new Microsoft.Kiota.Http.HttpClientLibrary.HttpClientRequestAdapter(
            new Microsoft.Kiota.Abstractions.Authentication.BaseBearerTokenAuthenticationProvider(
                new AzureIdentityAccessTokenProvider(authentication)));

    /// <summary>
    /// Resolves the configuration path. An explicit override wins, then an environment variable, then
    /// the file beside the executable.
    /// </summary>
    public static string ResolveConfigurationPath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("ADEXO_CLOUD_CONFIG");
        return !string.IsNullOrWhiteSpace(fromEnvironment)
            ? fromEnvironment
            : Path.Combine(AppContext.BaseDirectory, CloudConfigurationLoader.DefaultFileName);
    }
}

/// <summary>
/// Supplies the delegated access token to the Graph request pipeline. The token is acquired
/// immediately before each request and is not stored, logged, or exposed.
/// </summary>
internal sealed class AzureIdentityAccessTokenProvider : Microsoft.Kiota.Abstractions.Authentication.IAccessTokenProvider
{
    private readonly PublicClientAuthenticationService _authentication;

    public AzureIdentityAccessTokenProvider(PublicClientAuthenticationService authentication) =>
        _authentication = authentication;

    /// <summary>Graph is the only host this client talks to.</summary>
    public Microsoft.Kiota.Abstractions.Authentication.AllowedHostsValidator AllowedHostsValidator { get; } =
        new(["graph.microsoft.com"]);

    public Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default) =>
        _authentication.GetAccessTokenForRequestAsync(cancellationToken);
}
