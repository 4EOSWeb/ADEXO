namespace ADEXO.Contracts.Directory;

public enum IdentityStage
{
    LaunchReceived,
    LaunchValidated,
    InitialObjectResolved,
    GuidIdentityEstablished,
    GuidRebindVerified,
    DisplayDataLoaded,
    Failed,
}

public enum AccountEnabledState
{
    Unavailable,
    Enabled,
    Disabled,
}

public enum AccountExpirationKind
{
    Unavailable,
    Never,
    Date,
    Invalid,
}

public enum PasswordLastSetKind
{
    Unavailable,
    MustChangeAtNextLogon,
    Date,
}

public enum LockoutIndication
{
    Unavailable,
    NotIndicated,
    TimeSetOnThisReplica,
}

public sealed class DirectoryAttributeBag
{
    public DirectoryAttributeBag(IReadOnlyDictionary<string, IReadOnlyList<string>> text, IReadOnlyDictionary<string, byte[]> binary)
    {
        Text = text;
        Binary = binary;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Text { get; }

    public IReadOnlyDictionary<string, byte[]> Binary { get; }
}

public sealed class VerifiedDirectoryUser
{
    public required Guid ObjectGuid { get; init; }

    public required string DistinguishedName { get; init; }

    public required string ObjectClass { get; init; }

    public string? DisplayName { get; init; }

    public string? SamAccountName { get; init; }

    public string? UserPrincipalName { get; init; }

    public string? ObjectSid { get; init; }

    public string? CanonicalName { get; init; }

    public string? Name { get; init; }

    public string? GivenName { get; init; }

    public string? Surname { get; init; }

    public string? Initials { get; init; }

    public AccountEnabledState Enabled { get; init; }

    public AccountExpirationKind ExpirationKind { get; init; }

    public DateTimeOffset? ExpirationUtc { get; init; }

    public PasswordLastSetKind PasswordLastSetKind { get; init; }

    public DateTimeOffset? PasswordLastSetUtc { get; init; }

    public LockoutIndication Lockout { get; init; }

    public DateTimeOffset? WhenCreatedUtc { get; init; }

    public DateTimeOffset? WhenChangedUtc { get; init; }

    public string? Mail { get; init; }

    public string? TelephoneNumber { get; init; }

    public string? Mobile { get; init; }

    public string? IpPhone { get; init; }

    public string? StreetAddress { get; init; }

    public string? City { get; init; }

    public string? State { get; init; }

    public string? PostalCode { get; init; }

    public string? Country { get; init; }

    public string? Title { get; init; }

    public string? Department { get; init; }

    public string? Company { get; init; }

    public string? Office { get; init; }

    public string? ManagerDistinguishedName { get; init; }

    public Guid? ManagerObjectGuid { get; init; }

    public string? ManagerDisplayName { get; init; }

    public string? MailNickname { get; init; }

    public IReadOnlyList<string> ProxyAddresses { get; init; } = [];

    public string? TargetAddress { get; init; }

    public string? HiddenFromAddressLists { get; init; }

    public IReadOnlyDictionary<string, string?> ExtensionAttributes { get; init; } =
        new Dictionary<string, string?>();

    public IdentityStage Stage { get; init; } = IdentityStage.DisplayDataLoaded;
}
