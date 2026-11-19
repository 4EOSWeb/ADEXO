using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Results;

namespace ADEXO.Graph.Reads;

/// <summary>
/// The narrow read surface the provider composes against. It exists so the provider can be exercised
/// against a substitute without a live tenant, and so the request surface stays enumerable: three
/// reads, no URL, no method, no body.
/// </summary>
public interface IGraphReadClient
{
    Task<ProviderResult<Guid>> ReadOrganizationIdAsync(CancellationToken cancellationToken);

    Task<ProviderResult<IReadOnlyList<CloudDirectoryUser>>> ReadUsersByImmutableIdAsync(
        string escapedImmutableId,
        CancellationToken cancellationToken);

    Task<ProviderResult<IReadOnlyList<CloudSubscribedSku>>> ReadSubscribedSkusAsync(CancellationToken cancellationToken);
}
