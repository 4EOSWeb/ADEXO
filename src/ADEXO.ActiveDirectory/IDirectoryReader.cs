using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Launch;
using ADEXO.Contracts.Results;

namespace ADEXO.ActiveDirectory;

public interface IDirectoryReader
{
    Task<ProviderResult<TrustedDirectoryEndpoint>> ReadRootDseAsync(CancellationToken cancellationToken);

    Task<ProviderResult<DirectoryAttributeBag>> ReadByDistinguishedNameAsync(
        TrustedDirectoryEndpoint endpoint,
        DistinguishedName distinguishedName,
        CancellationToken cancellationToken);

    Task<ProviderResult<DirectoryAttributeBag>> ReadByObjectGuidAsync(
        TrustedDirectoryEndpoint endpoint,
        Guid objectGuid,
        CancellationToken cancellationToken);
}
