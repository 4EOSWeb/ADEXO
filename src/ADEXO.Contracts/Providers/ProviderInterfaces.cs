using ADEXO.Contracts.Capabilities;
using ADEXO.Contracts.Features;

namespace ADEXO.Contracts.Providers;

public interface IActiveDirectoryProvider
{
    Task<ProviderCapability> GetCapabilityAsync(KnownFeatureId feature, CancellationToken cancellationToken);
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
