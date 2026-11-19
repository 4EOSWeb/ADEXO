using ADEXO.Contracts.Cloud;
using ADEXO.Core.Licensing;

namespace ADEXO.Graph.Tests;

public sealed class LicenseResolverTests
{
    private static readonly Guid SkuA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid SkuB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid UnknownSku = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private static IReadOnlyDictionary<Guid, CloudSubscribedSku> Catalogue() => LicenseResolver.Index(
    [
        new CloudSubscribedSku { SkuId = SkuA, SkuPartNumber = "PART_A", DisplayName = "Tenant Name A" },
        new CloudSubscribedSku { SkuId = SkuB, SkuPartNumber = "PART_B" },
    ]);

    [Fact]
    public void No_licenses_is_a_definite_answer()
    {
        var (licenses, resolved) = LicenseResolver.Resolve([], Catalogue());

        Assert.Empty(licenses);
        Assert.True(resolved);
    }

    [Fact]
    public void One_license_is_named_from_the_catalogue()
    {
        var (licenses, resolved) = LicenseResolver.Resolve([SkuA], Catalogue());

        var license = Assert.Single(licenses);
        Assert.Equal("Tenant Name A", license.Label);
        Assert.False(license.IsUnresolved);
        Assert.True(resolved);
    }

    [Fact]
    public void Several_licenses_are_all_resolved()
    {
        var (licenses, resolved) = LicenseResolver.Resolve([SkuA, SkuB], Catalogue());

        Assert.Equal(2, licenses.Count);
        Assert.True(resolved);
        Assert.Equal(["Tenant Name A", "PART_B"], licenses.Select(l => l.Label));
    }

    [Fact]
    public void An_unknown_sku_shows_its_identifier_not_a_guess()
    {
        var (licenses, resolved) = LicenseResolver.Resolve([UnknownSku], Catalogue());

        var license = Assert.Single(licenses);
        Assert.True(license.IsUnresolved);
        Assert.Equal(UnknownSku.ToString("D"), license.Label);
        Assert.True(resolved);
    }

    [Fact]
    public void A_part_number_without_a_display_name_is_used()
    {
        var (licenses, _) = LicenseResolver.Resolve([SkuB], Catalogue());

        Assert.Equal("PART_B", licenses[0].Label);
        Assert.False(licenses[0].IsUnresolved);
    }

    [Fact]
    public void An_unavailable_catalogue_keeps_the_skus_and_says_so()
    {
        var (licenses, resolved) = LicenseResolver.Resolve([SkuA], catalogue: null);

        var license = Assert.Single(licenses);
        Assert.False(resolved);
        Assert.True(license.IsUnresolved);
        Assert.Equal(SkuA, license.SkuId);
    }

    [Fact]
    public void Duplicate_assignments_collapse()
    {
        var (licenses, _) = LicenseResolver.Resolve([SkuA, SkuA], Catalogue());

        Assert.Single(licenses);
    }

    [Fact]
    public void The_index_ignores_empty_identifiers_and_duplicates()
    {
        var index = LicenseResolver.Index(
        [
            new CloudSubscribedSku { SkuId = Guid.Empty, SkuPartNumber = "EMPTY" },
            new CloudSubscribedSku { SkuId = SkuA, SkuPartNumber = "FIRST" },
            new CloudSubscribedSku { SkuId = SkuA, SkuPartNumber = "SECOND" },
        ]);

        Assert.Single(index);
        Assert.Equal("FIRST", index[SkuA].SkuPartNumber);
    }

    [Fact]
    public void No_marketing_product_names_are_hard_coded_in_the_resolver()
    {
        // A friendly-name table would go stale and silently mislabel a renumbered SKU. The only source of
        // a name is the tenant catalogue.
        string[] marketing =
        [
            "Microsoft 365", "Office 365", "E3", "E5", "Business Premium", "Exchange Online Plan",
            "EMS", "Entra ID P1", "Entra ID P2",
        ];

        var source = File.ReadAllText(Path.Combine(FindRoot(), "src", "ADEXO.Core", "Licensing", "LicenseResolver.cs"));
        foreach (var name in marketing)
        {
            Assert.DoesNotContain(name, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ADEXO.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
