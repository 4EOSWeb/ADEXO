using ADEXO.Contracts.Cloud;

namespace ADEXO.Core.Licensing;

/// <summary>
/// Resolves the licences a verified user holds against the tenant's own SKU catalogue.
/// <para>
/// The catalogue is the only source of a licence name. There is no table of marketing product names in
/// this code, because such a table goes stale and would silently mislabel a renumbered SKU. When a SKU
/// is not in the catalogue, its identifiers are shown instead of a guessed name.
/// </para>
/// </summary>
public static class LicenseResolver
{
    /// <summary>
    /// Builds the display licences for a user.
    /// </summary>
    /// <param name="userLicenses">The SKU identifiers assigned to the user.</param>
    /// <param name="catalogue">The tenant catalogue, or null when it could not be read.</param>
    public static (IReadOnlyList<CloudLicenseAssignment> Licenses, bool CatalogueResolved) Resolve(
        IReadOnlyList<Guid> userLicenses,
        IReadOnlyDictionary<Guid, CloudSubscribedSku>? catalogue)
    {
        ArgumentNullException.ThrowIfNull(userLicenses);

        if (userLicenses.Count == 0)
        {
            // No licences is a definite answer, not an unresolved one.
            return ([], catalogue is not null);
        }

        if (catalogue is null)
        {
            // The identity result stands; only the names are unavailable.
            return (userLicenses
                .Select(skuId => new CloudLicenseAssignment(skuId, null, null, null))
                .ToList(), false);
        }

        var resolved = userLicenses
            .Distinct()
            .Select(skuId => catalogue.TryGetValue(skuId, out var sku)
                ? new CloudLicenseAssignment(skuId, sku.SkuPartNumber, sku.DisplayName, null)
                : new CloudLicenseAssignment(skuId, null, null, null))
            .ToList();

        return (resolved, true);
    }

    /// <summary>
    /// Indexes a catalogue for lookup. Duplicate identifiers keep the first entry, since they describe the
    /// same SKU.
    /// </summary>
    public static IReadOnlyDictionary<Guid, CloudSubscribedSku> Index(IReadOnlyList<CloudSubscribedSku> skus)
    {
        ArgumentNullException.ThrowIfNull(skus);

        var index = new Dictionary<Guid, CloudSubscribedSku>();
        foreach (var sku in skus)
        {
            if (sku.SkuId == Guid.Empty)
            {
                continue;
            }

            index.TryAdd(sku.SkuId, sku);
        }

        return index;
    }
}
