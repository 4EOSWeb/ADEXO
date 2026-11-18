namespace ADEXO.Contracts.Features;

/// <summary>
/// Production feature identifier. There is no public constructor, so call sites cannot invent spellings.
/// </summary>
public sealed class KnownFeatureId : IEquatable<KnownFeatureId>
{
    private static readonly List<KnownFeatureId> Catalog = [];

    private KnownFeatureId(string canonical)
    {
        if (string.IsNullOrWhiteSpace(canonical))
        {
            throw new ArgumentException("A feature id needs a canonical name.", nameof(canonical));
        }

        Canonical = canonical;
        Catalog.Add(this);
    }

    public string Canonical { get; }

    public static KnownFeatureId ActiveDirectoryUserProxyAddresses { get; } = new("ActiveDirectory.User.ProxyAddresses");

    public static KnownFeatureId MicrosoftGraphUserLicenseAssignment { get; } = new("MicrosoftGraph.User.LicenseAssignment");

    public static KnownFeatureId ExchangeOnlineMailboxForwarding { get; } = new("ExchangeOnline.Mailbox.Forwarding");

    public static KnownFeatureId ExchangeOnlineMailboxAutomaticReplies { get; } = new("ExchangeOnline.Mailbox.AutomaticReplies");

    public static KnownFeatureId EntraConnectSynchronizationDelta { get; } = new("EntraConnect.Synchronization.Delta");

    public static KnownFeatureId ActiveDirectoryUserIdentityRead { get; } = new("ActiveDirectory.User.Identity.Read");

    public static KnownFeatureId ActiveDirectoryUserAccountStateRead { get; } = new("ActiveDirectory.User.AccountState.Read");

    public static KnownFeatureId ActiveDirectoryUserContactRead { get; } = new("ActiveDirectory.User.Contact.Read");

    public static KnownFeatureId ActiveDirectoryUserOrganizationRead { get; } = new("ActiveDirectory.User.Organization.Read");

    public static KnownFeatureId ActiveDirectoryUserMetadataRead { get; } = new("ActiveDirectory.User.Metadata.Read");

    public static KnownFeatureId MicrosoftGraphUserIdentityRead { get; } = new("MicrosoftGraph.User.Identity.Read");

    public static KnownFeatureId MicrosoftGraphUserSyncStateRead { get; } = new("MicrosoftGraph.User.SyncState.Read");

    public static KnownFeatureId MicrosoftGraphUserLicenseRead { get; } = new("MicrosoftGraph.User.License.Read");

    public static KnownFeatureId MicrosoftGraphTenantVerification { get; } = new("MicrosoftGraph.Tenant.Verification");

    public static IReadOnlyList<KnownFeatureId> All => Catalog;

    public bool Equals(KnownFeatureId? other) =>
        other is not null && string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as KnownFeatureId);

    public override int GetHashCode() => string.GetHashCode(Canonical, StringComparison.Ordinal);

    public override string ToString() => Canonical;
}

/// <summary>
/// Identifier supplied at runtime by an extension. Production features use <see cref="KnownFeatureId"/>.
/// </summary>
public sealed class ExternalFeatureId : IEquatable<ExternalFeatureId>
{
    private ExternalFeatureId(string canonical) => Canonical = canonical;

    public string Canonical { get; }

    public static ExternalFeatureId Create(string canonical)
    {
        if (string.IsNullOrWhiteSpace(canonical))
        {
            throw new ArgumentException("An external feature id cannot be empty.", nameof(canonical));
        }

        return new ExternalFeatureId(canonical.Trim());
    }

    public bool Equals(ExternalFeatureId? other) =>
        other is not null && string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ExternalFeatureId);

    public override int GetHashCode() => string.GetHashCode(Canonical, StringComparison.Ordinal);

    public override string ToString() => Canonical;
}
