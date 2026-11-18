using ADEXO.Contracts.Capabilities;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Features;
using ADEXO.Contracts.Launch;
using ADEXO.Contracts.Results;

namespace ADEXO.Contracts.Providers;

public interface IActiveDirectoryProvider
{
    Task<ProviderCapability> GetCapabilityAsync(KnownFeatureId feature, CancellationToken cancellationToken);

    Task<ProviderResult<TrustedDirectoryEndpoint>> DiscoverAsync(CancellationToken cancellationToken);

    Task<ProviderResult<VerifiedDirectoryUser>> ResolveUserAsync(AducLaunchRequest request, CancellationToken cancellationToken);

    Task<ProviderResult<VerifiedDirectoryUser>> RefreshByObjectGuidAsync(Guid objectGuid, CancellationToken cancellationToken);
}

/// <summary>
/// Read-only Microsoft Graph access for Phase 3A.
/// <para>
/// Every member is a typed read. There is deliberately no member that accepts a URL, an OData
/// fragment, an HTTP method, or a request body, so a caller cannot compose an arbitrary request.
/// There is no create, update, or delete operation of any kind.
/// </para>
/// </summary>
public interface IGraphProvider
{
    Task<ProviderCapability> GetCapabilityAsync(KnownFeatureId feature, CancellationToken cancellationToken);

    /// <summary>
    /// Performs the directory half of the tenant lock: one organization read, selecting only the
    /// identifier. Available under the delegated <c>User.Read</c> scope, which Microsoft documents as
    /// sufficient to return the organization identifier.
    /// </summary>
    Task<ProviderResult<Guid>> GetOrganizationTenantIdAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finds cloud users whose synchronized immutable identifier equals the supplied value.
    /// The value is escaped before it reaches the query, and it is the only filter input accepted.
    /// </summary>
    Task<ProviderResult<IReadOnlyList<CloudDirectoryUser>>> FindUsersByImmutableIdAsync(
        string immutableId,
        CancellationToken cancellationToken);

    /// <summary>Reads the tenant SKU catalogue, used only to name the licences already assigned to a user.</summary>
    Task<ProviderResult<IReadOnlyList<CloudSubscribedSku>>> GetSubscribedSkusAsync(CancellationToken cancellationToken);
}

public interface IExchangeOnlineProvider
{
    Task<ProviderCapability> GetCapabilityAsync(KnownFeatureId feature, CancellationToken cancellationToken);
}

public interface IEntraConnectProvider
{
    Task<ProviderCapability> GetCapabilityAsync(KnownFeatureId feature, CancellationToken cancellationToken);
}
