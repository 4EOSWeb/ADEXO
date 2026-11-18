using ADEXO.Contracts.Capabilities;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Features;
using ADEXO.Contracts.Providers;
using ADEXO.Contracts.Results;
using ADEXO.Contracts.Time;

namespace ADEXO.Graph.Tests;

/// <summary>
/// A configurable stand-in for the Graph layer. It records every call so a test can prove that no
/// user query happened when a tenant lock failed.
/// </summary>
internal sealed class FakeGraphProvider : IGraphProvider
{
    public Guid DirectoryTenantId { get; set; } = Guid.Parse("c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af");

    public OperationError? OrganizationError { get; set; }

    public OperationError? UserQueryError { get; set; }

    public OperationError? SkuError { get; set; }

    public Dictionary<string, List<CloudDirectoryUser>> UsersByImmutableId { get; } = new(StringComparer.Ordinal);

    public List<CloudSubscribedSku> Skus { get; } = [];

    public List<string> ImmutableIdQueries { get; } = [];

    public int OrganizationReads { get; private set; }

    public int SkuReads { get; private set; }

    public bool UserWasQueried => ImmutableIdQueries.Count > 0;

    public Task<ProviderCapability> GetCapabilityAsync(KnownFeatureId feature, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderCapability.ReadOnly(
            ProviderKind.MicrosoftGraph,
            feature,
            Contracts.Authority.SourceOfAuthority.MicrosoftEntra,
            "test",
            DateTimeOffset.UnixEpoch));

    public Task<ProviderResult<Guid>> GetOrganizationTenantIdAsync(CancellationToken cancellationToken)
    {
        OrganizationReads++;
        return Task.FromResult(OrganizationError is null
            ? ProviderResult<Guid>.Success(DirectoryTenantId)
            : ProviderResult<Guid>.Failed(OrganizationError));
    }

    public Task<ProviderResult<IReadOnlyList<CloudDirectoryUser>>> FindUsersByImmutableIdAsync(
        string immutableId,
        CancellationToken cancellationToken)
    {
        ImmutableIdQueries.Add(immutableId);

        if (UserQueryError is not null)
        {
            return Task.FromResult(ProviderResult<IReadOnlyList<CloudDirectoryUser>>.Failed(UserQueryError));
        }

        var users = UsersByImmutableId.TryGetValue(immutableId, out var match) ? match : [];
        return Task.FromResult(ProviderResult<IReadOnlyList<CloudDirectoryUser>>.Success((IReadOnlyList<CloudDirectoryUser>)users));
    }

    public Task<ProviderResult<IReadOnlyList<CloudSubscribedSku>>> GetSubscribedSkusAsync(CancellationToken cancellationToken)
    {
        SkuReads++;
        return Task.FromResult(SkuError is null
            ? ProviderResult<IReadOnlyList<CloudSubscribedSku>>.Success((IReadOnlyList<CloudSubscribedSku>)Skus)
            : ProviderResult<IReadOnlyList<CloudSubscribedSku>>.Failed(SkuError));
    }
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; } = new(2026, 11, 18, 19, 0, 0, TimeSpan.Zero);
}
