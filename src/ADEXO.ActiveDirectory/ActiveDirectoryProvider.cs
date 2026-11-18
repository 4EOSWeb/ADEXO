using ADEXO.ActiveDirectory.Ldap;
using ADEXO.Contracts.Authority;
using ADEXO.Contracts.Capabilities;
using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Features;
using ADEXO.Contracts.Identity;
using ADEXO.Contracts.Launch;
using ADEXO.Contracts.Providers;
using ADEXO.Contracts.Results;
using ADEXO.Core.Launch;

namespace ADEXO.ActiveDirectory;

public sealed class ActiveDirectoryProvider : IActiveDirectoryProvider
{
    private readonly IDirectoryReader _reader;
    private readonly TimeProvider _time;

    public ActiveDirectoryProvider(IDirectoryReader reader)
        : this(reader, TimeProvider.System)
    {
    }

    public ActiveDirectoryProvider(IDirectoryReader reader, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(time);
        _reader = reader;
        _time = time;
    }

    public async Task<ProviderCapability> GetCapabilityAsync(KnownFeatureId feature, CancellationToken cancellationToken)
    {
        var stamp = _time.GetUtcNow();
        if (!IsReadFeature(feature))
        {
            return ProviderCapability.Unsupported(
                ProviderKind.ActiveDirectory,
                feature,
                SourceOfAuthority.Unsupported,
                "Phase 2 does not implement this feature.",
                stamp);
        }

        var discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (!discovery.IsSuccess)
        {
            if (discovery.Error?.Category == FailureCategory.Authorization)
            {
                return ProviderCapability.Unauthorized(
                    ProviderKind.ActiveDirectory,
                    feature,
                    SourceOfAuthority.ActiveDirectory,
                    "The signed-in account cannot read the directory.",
                    false,
                    stamp);
            }

            return ProviderCapability.Unavailable(
                ProviderKind.ActiveDirectory,
                feature,
                SourceOfAuthority.ActiveDirectory,
                discovery.Error?.Summary ?? "No domain controller could be reached.",
                stamp);
        }

        return ProviderCapability.ReadOnly(
            ProviderKind.ActiveDirectory,
            feature,
            SourceOfAuthority.ActiveDirectory,
            "Phase 2 reads Active Directory and does not write.",
            stamp);
    }

    public Task<ProviderResult<TrustedDirectoryEndpoint>> DiscoverAsync(CancellationToken cancellationToken) =>
        _reader.ReadRootDseAsync(cancellationToken);

    public async Task<ProviderResult<VerifiedDirectoryUser>> ResolveUserAsync(AducLaunchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ObjectClass.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderResult<VerifiedDirectoryUser>.Failed(OperationError.Create(FailureCategory.Unsupported, "Only a user object is supported."));
        }

        var discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (!discovery.IsSuccess || discovery.Value is null)
        {
            return Forward(discovery);
        }

        var selected = TrustedEndpointSelector.Select(request.Path, discovery.Value);
        if (!selected.Accepted || selected.Endpoint is null)
        {
            return ProviderResult<VerifiedDirectoryUser>.Failed(OperationError.Create(FailureCategory.Validation, selected.FailureMessage ?? "The directory endpoint was rejected."));
        }

        var first = await _reader.ReadByDistinguishedNameAsync(selected.Endpoint, request.Path.DistinguishedName, cancellationToken).ConfigureAwait(false);
        if (!first.IsSuccess || first.Value is null)
        {
            return Forward(first);
        }

        if (!UserMapper.IncludesUser(first.Value))
        {
            return ProviderResult<VerifiedDirectoryUser>.Failed(OperationError.Create(FailureCategory.Unsupported, "The object class is not user."));
        }

        var firstGuid = ReadGuid(first.Value);
        if (firstGuid is null)
        {
            return ProviderResult<VerifiedDirectoryUser>.Failed(OperationError.Create(FailureCategory.Validation, "The object did not include a valid objectGUID."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ProviderResult<VerifiedDirectoryUser>.Canceled();
        }

        var second = await _reader.ReadByObjectGuidAsync(selected.Endpoint, firstGuid.Value, cancellationToken).ConfigureAwait(false);
        if (!second.IsSuccess || second.Value is null)
        {
            return Forward(second);
        }

        var secondGuid = ReadGuid(second.Value);
        if (secondGuid != firstGuid)
        {
            return ProviderResult<VerifiedDirectoryUser>.Failed(OperationError.Create(FailureCategory.VerificationFailed, "GUID rebind did not return the same object."));
        }

        return ProviderResult<VerifiedDirectoryUser>.Success(await WithManagerAsync(selected.Endpoint, second.Value, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ProviderResult<VerifiedDirectoryUser>> RefreshByObjectGuidAsync(Guid objectGuid, CancellationToken cancellationToken)
    {
        var discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (!discovery.IsSuccess || discovery.Value is null)
        {
            return Forward(discovery);
        }

        var second = await _reader.ReadByObjectGuidAsync(discovery.Value, objectGuid, cancellationToken).ConfigureAwait(false);
        if (!second.IsSuccess || second.Value is null)
        {
            return Forward(second);
        }

        if (ReadGuid(second.Value) != objectGuid)
        {
            return ProviderResult<VerifiedDirectoryUser>.Failed(OperationError.Create(FailureCategory.VerificationFailed, "GUID refresh did not return the same object."));
        }

        return ProviderResult<VerifiedDirectoryUser>.Success(await WithManagerAsync(discovery.Value, second.Value, cancellationToken).ConfigureAwait(false));
    }

    private async Task<VerifiedDirectoryUser> WithManagerAsync(TrustedDirectoryEndpoint endpoint, DirectoryAttributeBag bag, CancellationToken cancellationToken)
    {
        Guid? managerGuid = null;
        string? managerName = null;
        if (bag.Text.TryGetValue("manager", out var managerValues) && managerValues.Count == 1
            && DistinguishedNameParser.TryParse(managerValues[0], out var managerDn, out _)
            && managerDn is not null)
        {
            var manager = await _reader.ReadByDistinguishedNameAsync(endpoint, managerDn, cancellationToken).ConfigureAwait(false);
            if (manager.IsSuccess && manager.Value is not null)
            {
                managerGuid = ReadGuid(manager.Value);
                managerName = manager.Value.Text.TryGetValue("displayName", out var names) ? names.FirstOrDefault() : null;
            }
        }

        var mapped = UserMapper.Map(bag, managerGuid, managerName);
        if (mapped is null)
        {
            throw new InvalidOperationException("The verified object could not be mapped.");
        }

        return mapped;
    }

    private static Guid? ReadGuid(DirectoryAttributeBag bag) =>
        bag.Binary.TryGetValue("objectGUID", out var bytes) ? AttributeConverter.ReadGuid(bytes) : null;

    private static ProviderResult<VerifiedDirectoryUser> Forward<TIn>(ProviderResult<TIn> result)
    {
        if (result.IsCanceled)
        {
            return ProviderResult<VerifiedDirectoryUser>.Canceled();
        }

        return ProviderResult<VerifiedDirectoryUser>.Failed(result.Error ?? OperationError.Create(FailureCategory.Unknown, "The directory request failed."));
    }

    private static bool IsReadFeature(KnownFeatureId feature) =>
        feature.Equals(KnownFeatureId.ActiveDirectoryUserIdentityRead)
        || feature.Equals(KnownFeatureId.ActiveDirectoryUserAccountStateRead)
        || feature.Equals(KnownFeatureId.ActiveDirectoryUserContactRead)
        || feature.Equals(KnownFeatureId.ActiveDirectoryUserOrganizationRead)
        || feature.Equals(KnownFeatureId.ActiveDirectoryUserMetadataRead);
}
