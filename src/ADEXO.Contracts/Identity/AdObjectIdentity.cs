namespace ADEXO.Contracts.Identity;

/// <summary>
/// Durable identity of an Active Directory object. Equality uses <see cref="ObjectGuid"/> only.
/// </summary>
public sealed class AdObjectIdentity : IEquatable<AdObjectIdentity>
{
    public AdObjectIdentity(
        Guid objectGuid,
        string distinguishedName,
        string adsPath,
        string objectClass,
        string? securityIdentifier)
    {
        if (objectGuid == Guid.Empty)
        {
            throw new ArgumentException("objectGUID is the durable identity and is required.", nameof(objectGuid));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(distinguishedName);
        ArgumentException.ThrowIfNullOrWhiteSpace(adsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectClass);
        ObjectGuid = objectGuid;
        DistinguishedName = distinguishedName;
        AdsPath = adsPath;
        ObjectClass = objectClass;
        SecurityIdentifier = securityIdentifier;
    }

    public Guid ObjectGuid { get; }

    public string DistinguishedName { get; }

    public string AdsPath { get; }

    public string ObjectClass { get; }

    public string? SecurityIdentifier { get; }

    public bool Equals(AdObjectIdentity? other) => other is not null && ObjectGuid.Equals(other.ObjectGuid);

    public override bool Equals(object? obj) => Equals(obj as AdObjectIdentity);

    public override int GetHashCode() => ObjectGuid.GetHashCode();
}
