using ADEXO.Contracts.Capabilities;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Configuration;
using ADEXO.Contracts.Features;
using ADEXO.Contracts.Providers;
using ADEXO.Contracts.Results;
using ADEXO.Contracts.Time;
using ADEXO.Core.Correlation;
using ADEXO.Graph.Reads;

namespace ADEXO.Graph;

/// <summary>
/// The Phase 3A Microsoft Graph provider: read-only, tenant-locked, and typed.
/// <para>
/// The public surface is three reads. There is no URL, OData, method, or body parameter anywhere, and
/// no create, update, or delete operation exists, so a caller cannot compose a request the design did
/// not anticipate. The tenant lock is enforced structurally: correlation requires a verified
/// <see cref="TenantVerificationResult"/>, and that type can only be produced by the lock itself.
/// </para>
/// </summary>
public sealed class GraphCloudProvider : IGraphProvider
{
    private readonly IGraphReadClient _client;
    private readonly IClock _clock;

    public GraphCloudProvider(IGraphReadClient client, IClock clock)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<ProviderCapability> GetCapabilityAsync(KnownFeatureId feature, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return Task.FromResult(ProviderCapability.ReadOnly(
            ProviderKind.MicrosoftGraph,
            feature,
            Contracts.Authority.SourceOfAuthority.MicrosoftEntra,
            "Phase 3A reads Microsoft Entra ID and does not write.",
            _clock.UtcNow));
    }

    /// <summary>The directory half of the tenant lock (T2). Reads only the organization identifier.</summary>
    public Task<ProviderResult<Guid>> GetOrganizationTenantIdAsync(CancellationToken cancellationToken) =>
        _client.ReadOrganizationIdAsync(cancellationToken);

    /// <summary>
    /// Finds cloud users by synchronized immutable identifier. The value is escaped here, so callers
    /// pass a raw candidate value and never filter text.
    /// </summary>
    public Task<ProviderResult<IReadOnlyList<CloudDirectoryUser>>> FindUsersByImmutableIdAsync(
        string immutableId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(immutableId);
        return _client.ReadUsersByImmutableIdAsync(ImmutableIdCandidate.EscapeODataLiteral(immutableId), cancellationToken);
    }

    public Task<ProviderResult<IReadOnlyList<CloudSubscribedSku>>> GetSubscribedSkusAsync(CancellationToken cancellationToken) =>
        _client.ReadSubscribedSkusAsync(cancellationToken);

    /// <summary>
    /// Correlates a verified Active Directory object to a cloud object. This is the only correlation
    /// entry point, and it refuses to run unless both tenant locks held.
    /// </summary>
    /// <returns>
    /// The verdict, plus the cloud user when the verdict is verified and exactly one object matched.
    /// </returns>
    public async Task<(CloudCorrelationResult Correlation, CloudDirectoryUser? User)> CorrelateAsync(
        AdCorrelationSubject subject,
        TenantVerificationResult tenant,
        byte[]? consistencyGuid,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(tenant);

        // Structural guard: a failed or partial lock cannot reach a selected-user query, because this
        // returns before any request is composed.
        if (!tenant.IsVerified)
        {
            return (CloudCorrelationResult.TenantMismatch([]), null);
        }

        var candidates = ImmutableIdCandidateFactory.Create(subject.ObjectGuid, consistencyGuid);
        var results = new List<IReadOnlyList<CloudDirectoryUser>>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var found = await FindUsersByImmutableIdAsync(candidate.Value, cancellationToken).ConfigureAwait(false);
            if (found.IsCanceled)
            {
                return (CloudCorrelationResult.NotEvaluated([]), null);
            }

            if (!found.IsSuccess)
            {
                // A failed query is not the same as no match. Letting it read as an empty batch would
                // make a permission error look like "not synchronized", so it is surfaced instead.
                throw new GraphReadException(found.Error!);
            }

            results.Add(found.Value ?? []);
        }

        var correlation = CloudCorrelationEvaluator.Evaluate(subject, results, tenant.IsVerified);

        // The selected object is returned only when the verdict is verified, and only the exact object
        // the evaluator accepted, so a refusal cannot leak a candidate's data.
        var user = correlation.IsVerified
            ? results.SelectMany(batch => batch).FirstOrDefault(u => u.EntraObjectId == correlation.EntraObjectId)
            : null;

        return (correlation, user);
    }

    /// <summary>
    /// Names the licences a verified user already holds, using the tenant catalogue. A catalogue
    /// failure leaves the identity result intact and the licences unresolved, which the UI states
    /// explicitly rather than showing a guessed product name.
    /// </summary>
    public async Task<CloudDirectoryUser> ResolveLicenseNamesAsync(CloudDirectoryUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Licenses.Count == 0)
        {
            return user;
        }

        var skus = await GetSubscribedSkusAsync(cancellationToken).ConfigureAwait(false);
        if (!skus.IsSuccess || skus.Value is null)
        {
            return user;
        }

        var catalogue = skus.Value.ToDictionary(sku => sku.SkuId);
        var resolved = user.Licenses
            .Select(license => catalogue.TryGetValue(license.SkuId, out var sku)
                ? new CloudLicenseAssignment(license.SkuId, sku.SkuPartNumber, sku.DisplayName, servicePlanSummary: null)
                : license)
            .ToList();

        return new CloudDirectoryUser
        {
            EntraObjectId = user.EntraObjectId,
            DisplayName = user.DisplayName,
            UserPrincipalName = user.UserPrincipalName,
            Mail = user.Mail,
            AccountEnabled = user.AccountEnabled,
            UsageLocation = user.UsageLocation,
            OnPremisesSyncEnabled = user.OnPremisesSyncEnabled,
            OnPremisesLastSyncDateTime = user.OnPremisesLastSyncDateTime,
            OnPremisesImmutableId = user.OnPremisesImmutableId,
            OnPremisesDistinguishedName = user.OnPremisesDistinguishedName,
            OnPremisesSecurityIdentifier = user.OnPremisesSecurityIdentifier,
            OnPremisesSamAccountName = user.OnPremisesSamAccountName,
            ProxyAddresses = user.ProxyAddresses,
            Licenses = resolved,
            LicenseCatalogueResolved = true,
        };
    }
}

/// <summary>
/// Raised when a Graph read fails in a way that must not be mistaken for an absence of data.
/// </summary>
public sealed class GraphReadException : Exception
{
    public GraphReadException(OperationError error)
        : base(error.Summary) => Error = error ?? throw new ArgumentNullException(nameof(error));

    public OperationError Error { get; }
}
