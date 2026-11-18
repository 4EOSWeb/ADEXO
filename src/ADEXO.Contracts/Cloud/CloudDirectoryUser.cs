using ADEXO.Contracts.Authority;

namespace ADEXO.Contracts.Cloud;

/// <summary>
/// A licence assigned to a cloud user, resolved against the tenant SKU catalogue where possible.
/// The friendly name comes from Graph data only. When the SKU is unknown the raw identifiers are
/// shown rather than inventing a marketing name.
/// </summary>
public sealed class CloudLicenseAssignment
{
    public CloudLicenseAssignment(Guid skuId, string? skuPartNumber, string? displayName, string? servicePlanSummary)
    {
        SkuId = skuId;
        SkuPartNumber = skuPartNumber;
        DisplayName = displayName;
        ServicePlanSummary = servicePlanSummary;
    }

    public Guid SkuId { get; }

    /// <summary>The tenant's own part number, when the SKU catalogue resolved.</summary>
    public string? SkuPartNumber { get; }

    /// <summary>The SKU display name from the tenant catalogue, when available.</summary>
    public string? DisplayName { get; }

    /// <summary>A short count or summary of the enabled service plans, for display only.</summary>
    public string? ServicePlanSummary { get; }

    /// <summary>
    /// True when the SKU catalogue could not resolve this identifier. The UI shows the raw GUID
    /// instead of a guessed product name.
    /// </summary>
    public bool IsUnresolved => string.IsNullOrWhiteSpace(SkuPartNumber) && string.IsNullOrWhiteSpace(DisplayName);

    /// <summary>The best available label: catalogue name, then part number, then the identifier.</summary>
    public string Label =>
        !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
        : !string.IsNullOrWhiteSpace(SkuPartNumber) ? SkuPartNumber
        : SkuId.ToString();
}

/// <summary>
/// Read-only cloud state for one correlated user. Every member is display data.
/// There is no write path, and <see cref="CanWrite"/> is always false in Phase 3A.
/// </summary>
public sealed class CloudDirectoryUser
{
    public required Guid EntraObjectId { get; init; }

    public string? DisplayName { get; init; }

    public string? UserPrincipalName { get; init; }

    public string? Mail { get; init; }

    public bool? AccountEnabled { get; init; }

    public string? UsageLocation { get; init; }

    public bool? OnPremisesSyncEnabled { get; init; }

    public DateTimeOffset? OnPremisesLastSyncDateTime { get; init; }

    public string? OnPremisesImmutableId { get; init; }

    public string? OnPremisesDistinguishedName { get; init; }

    public string? OnPremisesSecurityIdentifier { get; init; }

    public string? OnPremisesSamAccountName { get; init; }

    public IReadOnlyList<string> ProxyAddresses { get; init; } = [];

    public IReadOnlyList<CloudLicenseAssignment> Licenses { get; init; } = [];

    /// <summary>
    /// Whether the SKU catalogue was consulted. When false, licences carry raw identifiers only,
    /// and the UI says so rather than implying the names are unknown because the user has none.
    /// </summary>
    public bool LicenseCatalogueResolved { get; init; }

    /// <summary>Microsoft Entra is authoritative for the cloud-side values shown here.</summary>
    public SourceOfAuthority Source { get; init; } = SourceOfAuthority.MicrosoftEntra;

    /// <summary>Phase 3A is read-only. This is a constant, not a permission check.</summary>
    public bool CanWrite => false;
}

/// <summary>
/// A tenant SKU from the subscribed SKU catalogue. Used only to translate a licence identifier
/// into the tenant's own naming.
/// </summary>
public sealed class CloudSubscribedSku
{
    public required Guid SkuId { get; init; }

    public string? SkuPartNumber { get; init; }

    public string? DisplayName { get; init; }

    public int? ConsumedUnits { get; init; }

    public int? EnabledUnits { get; init; }

    public int? SuspendedUnits { get; init; }

    public int? WarningUnits { get; init; }
}
