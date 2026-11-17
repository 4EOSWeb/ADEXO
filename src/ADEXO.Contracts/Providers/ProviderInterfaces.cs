using ADEXO.Contracts.Capabilities;
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

public interface IGraphProvider
{
    Task<ProviderCapability> GetCapabilityAsync(KnownFeatureId feature, CancellationToken cancellationToken);
}

public interface IExchangeOnlineProvider
{
    Task<ProviderCapability> GetCapabilityAsync(KnownFeatureId feature, CancellationToken cancellationToken);
}

public interface IEntraConnectProvider
{
    Task<ProviderCapability> GetCapabilityAsync(KnownFeatureId feature, CancellationToken cancellationToken);
}
