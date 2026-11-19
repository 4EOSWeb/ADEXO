using ADEXO.Contracts.Authority;

namespace ADEXO.Contracts.Mailbox;

/// <summary>The kind of recipient the Exchange object turned out to be.</summary>
public enum MailboxKind
{
    /// <summary>Not looked up yet, or Exchange is not connected.</summary>
    NotEvaluated,

    UserMailbox,
    SharedMailbox,
    RoomMailbox,
    EquipmentMailbox,

    /// <summary>The user exists in Entra but has no Exchange recipient.</summary>
    NoMailbox,

    /// <summary>A recipient exists but is a type this phase does not model, such as a contact or group.</summary>
    UnsupportedRecipient,

    /// <summary>The returned object did not correspond to the verified Entra identity.</summary>
    MailboxIdentityConflict,
}

/// <summary>How a mailbox forwards mail, expressed as the distinct states an administrator must distinguish.</summary>
public enum ForwardingKind
{
    NotEvaluated,
    None,
    InternalForwarding,
    ExternalForwarding,
    DeliverAndForward,
    Unavailable,
}

/// <summary>Archive state as Exchange reports it.</summary>
public enum ArchiveState
{
    NotEvaluated,
    Disabled,
    Enabled,
    Unavailable,
}

/// <summary>Availability of the mailbox statistics read. Usage can be missing without the mailbox being missing.</summary>
public enum MailboxUsageState
{
    NotEvaluated,
    Available,
    StatisticsUnavailable,
}

/// <summary>
/// One mailbox row. Every member is display data read from Exchange; nothing here is writable, and
/// <see cref="CanWrite"/> is a constant false in Phase 3.
/// </summary>
public sealed class MailboxDetails
{
    public MailboxKind Kind { get; init; } = MailboxKind.NotEvaluated;

    public Guid? ExternalDirectoryObjectId { get; init; }

    public Guid? ExchangeGuid { get; init; }

    public string? RecipientTypeDetails { get; init; }

    public string? PrimarySmtpAddress { get; init; }

    public string? Alias { get; init; }

    public bool? HiddenFromAddressListsEnabled { get; init; }

    public ArchiveState Archive { get; init; } = ArchiveState.NotEvaluated;

    public Guid? ArchiveGuid { get; init; }

    public ForwardingKind Forwarding { get; init; } = ForwardingKind.NotEvaluated;

    /// <summary>The internal forwarding target, when forwarding is to another mailbox.</summary>
    public string? ForwardingAddress { get; init; }

    /// <summary>The external forwarding target, when forwarding leaves the organization.</summary>
    public string? ForwardingSmtpAddress { get; init; }

    public bool? DeliverToMailboxAndForward { get; init; }

    public bool? MessageCopyForSentAsEnabled { get; init; }

    public bool? MessageCopyForSendOnBehalfEnabled { get; init; }

    /// <summary>Every proxy address Exchange holds, in the order Exchange returned them.</summary>
    public IReadOnlyList<string> EmailAddresses { get; init; } = [];

    public MailboxUsageState UsageState { get; init; } = MailboxUsageState.NotEvaluated;

    public MailboxUsage? Usage { get; init; }

    /// <summary>True when forwarding sends mail outside the organization. Drives the UI warning.</summary>
    public bool IsExternalForwarding => Forwarding == ForwardingKind.ExternalForwarding;

    public SourceOfAuthority Source { get; init; } = SourceOfAuthority.ExchangeOnline;

    /// <summary>Phase 3 is read-only. A constant, not a permission check.</summary>
    public bool CanWrite => false;
}

/// <summary>
/// Mailbox operation metadata. These timestamps describe mailbox activity, not a person's working
/// hours, attendance, or productivity, and the UI labels them accordingly.
/// </summary>
public sealed class MailboxUsage
{
    public long? TotalItemBytes { get; init; }

    public string? TotalItemSizeText { get; init; }

    public int? ItemCount { get; init; }

    public int? DeletedItemCount { get; init; }

    public DateTimeOffset? LastLogonTime { get; init; }

    public DateTimeOffset? LastUserActionTime { get; init; }

    public string? StorageLimitStatus { get; init; }
}

/// <summary>Permission kinds Exchange distinguishes. They are kept separate because they grant different powers.</summary>
public enum DelegationKind
{
    FullAccess,
    SendAs,
    SendOnBehalf,
}

/// <summary>One delegation grant, read only.</summary>
public sealed class DelegationEntry
{
    public DelegationKind Kind { get; init; }

    /// <summary>The trustee as Exchange reported it.</summary>
    public required string Trustee { get; init; }

    /// <summary>True when the trustee could be resolved to a name this session can display.</summary>
    public bool IsResolved { get; init; }

    public string? DisplayName { get; init; }
}

/// <summary>Whether a delegation read was possible at all, and why not when it was not.</summary>
public enum DelegationState
{
    NotEvaluated,
    Available,
    /// <summary>The signed-in administrator lacks the Exchange role required for that read.</summary>
    PermissionMissing,
    /// <summary>The read is implemented but its role requirement was not verified, so it is not attempted.</summary>
    Deferred,
    Unavailable,
}

/// <summary>Delegation results, held per kind so one unavailable kind does not erase the others.</summary>
public sealed class DelegationReadResult
{
    public DelegationState State { get; init; } = DelegationState.NotEvaluated;

    public IReadOnlyList<DelegationEntry> FullAccess { get; init; } = [];

    public IReadOnlyList<DelegationEntry> SendAs { get; init; } = [];

    public IReadOnlyList<DelegationEntry> SendOnBehalf { get; init; } = [];

    public string? Reason { get; init; }
}
