namespace ADEXO.Contracts.Identity;

/// <summary>
/// Durable cloud object identity. The user principal name is display data, not the identity.
/// </summary>
public sealed class CloudObjectIdentity : IEquatable<CloudObjectIdentity>
{
    public CloudObjectIdentity(Guid objectId, string? userPrincipalName, Guid? externalDirectoryObjectId)
    {
        if (objectId == Guid.Empty)
        {
            throw new ArgumentException("The cloud object id is required.", nameof(objectId));
        }

        ObjectId = objectId;
        UserPrincipalName = userPrincipalName;
        ExternalDirectoryObjectId = externalDirectoryObjectId;
    }

    public Guid ObjectId { get; }

    public string? UserPrincipalName { get; }

    public Guid? ExternalDirectoryObjectId { get; }

    public bool Equals(CloudObjectIdentity? other) => other is not null && ObjectId.Equals(other.ObjectId);

    public override bool Equals(object? obj) => Equals(obj as CloudObjectIdentity);

    public override int GetHashCode() => ObjectId.GetHashCode();
}

/// <summary>
/// Link from an Active Directory objectGUID to a verified cloud object.
/// </summary>
public sealed class CloudRelationship
{
    public CloudRelationship(Guid objectGuid, Guid cloudObjectId, bool isVerified)
    {
        if (objectGuid == Guid.Empty)
        {
            throw new ArgumentException("objectGUID is required.", nameof(objectGuid));
        }

        if (cloudObjectId == Guid.Empty)
        {
            throw new ArgumentException("The cloud object id is required.", nameof(cloudObjectId));
        }

        ObjectGuid = objectGuid;
        CloudObjectId = cloudObjectId;
        IsVerified = isVerified;
    }

    public Guid ObjectGuid { get; }

    public Guid CloudObjectId { get; }

    public bool IsVerified { get; }

    public static void EnsureWritable(CloudRelationship? relationship)
    {
        if (relationship is not { IsVerified: true })
        {
            throw new InvalidOperationException("A cloud write requires a verified cloud relationship.");
        }
    }
}
