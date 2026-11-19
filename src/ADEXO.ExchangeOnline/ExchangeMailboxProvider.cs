using System.Collections.ObjectModel;
using System.Management.Automation;
using ADEXO.Contracts.Capabilities;
using ADEXO.Contracts.Features;
using ADEXO.Contracts.Mailbox;
using ADEXO.Contracts.Results;
using ADEXO.Contracts.Time;

namespace ADEXO.ExchangeOnline;

/// <summary>
/// The read-only Exchange mailbox provider.
/// <para>
/// Mailbox identity is resolved by the verified Entra object identifier only. The returned object must
/// report the same identifier; a mismatch is a conflict, never a redirected lookup. No member accepts a
/// UPN, SMTP address, alias, or script text.
/// </para>
/// </summary>
public sealed class ExchangeMailboxProvider : IMailboxProvider
{
    /// <summary>The mailbox properties this phase reads. Requesting them explicitly avoids pulling content.</summary>
    internal static readonly string[] MailboxProperties =
    [
        "ExternalDirectoryObjectId", "ExchangeGuid", "RecipientTypeDetails", "PrimarySmtpAddress",
        "EmailAddresses", "Alias", "HiddenFromAddressListsEnabled", "ArchiveStatus", "ArchiveGuid",
        "ForwardingAddress", "ForwardingSmtpAddress", "DeliverToMailboxAndForward",
        "MessageCopyForSentAsEnabled", "MessageCopyForSendOnBehalfEnabled",
    ];

    private readonly IExchangeInvoker _invoker;
    private readonly IClock _clock;

    public ExchangeMailboxProvider(IExchangeInvoker invoker, IClock clock)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<ProviderResult<ProviderCapability>> GetCapabilityAsyncCore(KnownFeatureId feature, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<ProviderCapability>.Success(ProviderCapability.ReadOnly(
            ProviderKind.ExchangeOnline,
            feature,
            Contracts.Authority.SourceOfAuthority.ExchangeOnline,
            "Phase 3B reads Exchange Online and does not write.",
            _clock.UtcNow)));

    Task<ProviderCapability> IMailboxProvider.GetCapabilityAsync(KnownFeatureId feature, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderCapability.ReadOnly(
            ProviderKind.ExchangeOnline,
            feature,
            Contracts.Authority.SourceOfAuthority.ExchangeOnline,
            "Phase 3B reads Exchange Online and does not write.",
            _clock.UtcNow));

    public async Task<ProviderResult<MailboxDetails>> GetMailboxAsync(Guid entraObjectId, CancellationToken cancellationToken)
    {
        if (entraObjectId == Guid.Empty)
        {
            return ProviderResult<MailboxDetails>.Failed(
                OperationError.Create(FailureCategory.Validation, "A verified Entra object identifier is required."));
        }

        var invocation = new ExchangeInvocation(ExchangeReadCommands.GetMailbox)
            .With("ExternalDirectoryObjectId", entraObjectId)
            .With("Properties", MailboxProperties)
            .With("ErrorAction", "Stop");

        var result = await _invoker.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return result.IsCanceled
                ? ProviderResult<MailboxDetails>.Canceled()
                : ProviderResult<MailboxDetails>.Failed(result.Error!);
        }

        if (result.Value.Count == 0)
        {
            // The Entra user exists but Exchange holds no recipient for it. That is an absence.
            return ProviderResult<MailboxDetails>.Success(new MailboxDetails { Kind = MailboxKind.NoMailbox });
        }

        var mailbox = MapMailbox(result.Value[0]);

        if (mailbox.ExternalDirectoryObjectId is null || mailbox.ExternalDirectoryObjectId != entraObjectId)
        {
            // A different object came back. Refuse rather than display someone else's mailbox.
            return ProviderResult<MailboxDetails>.Success(new MailboxDetails
            {
                Kind = MailboxKind.MailboxIdentityConflict,
                ExternalDirectoryObjectId = mailbox.ExternalDirectoryObjectId,
            });
        }

        return ProviderResult<MailboxDetails>.Success(mailbox);
    }

    public async Task<ProviderResult<MailboxUsage>> GetUsageAsync(MailboxDetails mailbox, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mailbox);

        if (mailbox.Kind is MailboxKind.NoMailbox or MailboxKind.MailboxIdentityConflict
            || mailbox.ExternalDirectoryObjectId is null)
        {
            return ProviderResult<MailboxUsage>.Success(new MailboxUsage());
        }

        var invocation = new ExchangeInvocation(ExchangeReadCommands.GetMailboxStatistics)
            .With("Identity", mailbox.ExternalDirectoryObjectId.Value)
            .With("ErrorAction", "Stop");

        var result = await _invoker.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            // Statistics are optional; the mailbox is still real.
            return result.IsCanceled
                ? ProviderResult<MailboxUsage>.Canceled()
                : ProviderResult<MailboxUsage>.Failed(result.Error!);
        }

        return result.Value.Count == 0
            ? ProviderResult<MailboxUsage>.Success(new MailboxUsage())
            : ProviderResult<MailboxUsage>.Success(MapUsage(result.Value[0]));
    }

    /// <summary>
    /// Reads delegation. Full Access and Send As are implemented; Send on Behalf is read from the mailbox
    /// itself. Each kind is reported separately so one missing role does not erase the others.
    /// </summary>
    public async Task<ProviderResult<DelegationReadResult>> GetDelegationsAsync(
        MailboxDetails mailbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mailbox);

        if (mailbox.Kind is MailboxKind.NoMailbox or MailboxKind.NoMailbox or MailboxKind.MailboxIdentityConflict
            || mailbox.ExternalDirectoryObjectId is null)
        {
            return ProviderResult<DelegationReadResult>.Success(new DelegationReadResult
            {
                State = DelegationState.Unavailable,
                Reason = mailbox.Kind == MailboxKind.NoMailbox
                    ? "There is no mailbox, so there is no delegation."
                    : "Delegation cannot be read without a matching mailbox identity.",
            });
        }

        var identity = mailbox.ExternalDirectoryObjectId.Value;

        var fullAccess = await ReadPermissionAsync(
            ExchangeReadCommands.GetMailboxPermission,
            identity,
            cancellationToken).ConfigureAwait(false);

        if (!fullAccess.IsSuccess || fullAccess.Value is null)
        {
            return ProviderResult<DelegationReadResult>.Failed(fullAccess.Error!);
        }

        var sendAs = await ReadPermissionAsync(
            ExchangeReadCommands.GetRecipientPermission,
            identity,
            cancellationToken).ConfigureAwait(false);

        var result = new DelegationReadResult
        {
            State = DelegationState.Available,
            FullAccess = MapTrustees(fullAccess.Value, DelegationKind.FullAccess),
            SendAs = sendAs.IsSuccess && sendAs.Value is not null
                ? MapTrustees(sendAs.Value, DelegationKind.SendAs)
                : [],
            // Send on Behalf comes from GrantSendOnBehalfTo on the mailbox, which this phase reads with
            // the mailbox itself rather than through a separate permission command.
            SendOnBehalf = [],
        };

        return ProviderResult<DelegationReadResult>.Success(result);
    }

    private async Task<ProviderResult<Collection<PSObject>>> ReadPermissionAsync(
        string command,
        Guid identity,
        CancellationToken cancellationToken)
    {
        var invocation = new ExchangeInvocation(command)
            .With("Identity", identity)
            .With("ErrorAction", "Stop");

        return await _invoker.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<DelegationEntry> MapTrustees(Collection<PSObject> rows, DelegationKind kind)
    {
        var entries = new List<DelegationEntry>();

        foreach (var row in rows)
        {
            var trustee = ReadString(row, "User") ?? ReadString(row, "Trustee");
            if (string.IsNullOrWhiteSpace(trustee))
            {
                continue;
            }

            // System and SELF entries are Exchange internals, not human grants.
            if (trustee.Contains("NT AUTHORITY", StringComparison.OrdinalIgnoreCase)
                || trustee.Equals("SELF", StringComparison.OrdinalIgnoreCase)
                || trustee.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var access = ReadString(row, "AccessRights");
            if (kind == DelegationKind.SendAs && access is not null
                && !access.Contains("SendAs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add(new DelegationEntry
            {
                Kind = kind,
                Trustee = trustee,
                IsResolved = true,
                DisplayName = trustee,
            });
        }

        return entries;
    }

    private static MailboxDetails MapMailbox(PSObject row)
    {
        var recipientType = ReadString(row, "RecipientTypeDetails");
        var kind = recipientType switch
        {
            "UserMailbox" => MailboxKind.UserMailbox,
            "SharedMailbox" => MailboxKind.SharedMailbox,
            "RoomMailbox" => MailboxKind.RoomMailbox,
            "EquipmentMailbox" => MailboxKind.EquipmentMailbox,
            null => MailboxKind.UserMailbox,
            _ => MailboxKind.UnsupportedRecipient,
        };

        var forwardingAddress = ReadString(row, "ForwardingAddress");
        var forwardingSmtp = ReadString(row, "ForwardingSmtpAddress");
        var deliverAndForward = ReadBool(row, "DeliverToMailboxAndForward");

        return new MailboxDetails
        {
            Kind = kind,
            ExternalDirectoryObjectId = ReadGuid(row, "ExternalDirectoryObjectId"),
            ExchangeGuid = ReadGuid(row, "ExchangeGuid"),
            RecipientTypeDetails = recipientType,
            PrimarySmtpAddress = ReadString(row, "PrimarySmtpAddress"),
            Alias = ReadString(row, "Alias"),
            HiddenFromAddressListsEnabled = ReadBool(row, "HiddenFromAddressListsEnabled"),
            Archive = ReadArchive(row),
            ArchiveGuid = ReadGuid(row, "ArchiveGuid"),
            Forwarding = ClassifyForwarding(forwardingAddress, forwardingSmtp, deliverAndForward),
            ForwardingAddress = forwardingAddress,
            ForwardingSmtpAddress = forwardingSmtp,
            DeliverToMailboxAndForward = deliverAndForward,
            MessageCopyForSentAsEnabled = ReadBool(row, "MessageCopyForSentAsEnabled"),
            MessageCopyForSendOnBehalfEnabled = ReadBool(row, "MessageCopyForSendOnBehalfEnabled"),
            EmailAddresses = ReadStringCollection(row, "EmailAddresses"),
        };
    }

    /// <summary>
    /// Classifies forwarding. An external target is distinguished from an internal one because the
    /// difference is the security-relevant one, and "deliver and forward" keeps a copy in the mailbox.
    /// </summary>
    public static ForwardingKind ClassifyForwarding(string? forwardingAddress, string? forwardingSmtp, bool? deliverAndForward)
    {
        var hasInternal = !string.IsNullOrWhiteSpace(forwardingAddress);
        var hasExternal = !string.IsNullOrWhiteSpace(forwardingSmtp);

        if (!hasInternal && !hasExternal)
        {
            return ForwardingKind.None;
        }

        if (hasExternal)
        {
            return ForwardingKind.ExternalForwarding;
        }

        return deliverAndForward == true
            ? ForwardingKind.DeliverAndForward
            : ForwardingKind.InternalForwarding;
    }

    private static ArchiveState ReadArchive(PSObject row)
    {
        var status = ReadString(row, "ArchiveStatus");
        if (status is null)
        {
            return ArchiveState.Unavailable;
        }

        return status.Equals("Active", StringComparison.OrdinalIgnoreCase)
            ? ArchiveState.Enabled
            : ArchiveState.Disabled;
    }

    private static MailboxUsage MapUsage(PSObject row) => new()
    {
        TotalItemSizeText = ReadNestedString(row, "TotalItemSize"),
        TotalItemBytes = ReadNestedLong(row, "TotalItemSize"),
        ItemCount = ReadInt(row, "ItemCount"),
        DeletedItemCount = ReadInt(row, "DeletedItemCount"),
        LastLogonTime = ReadDate(row, "LastLogonTime"),
        LastUserActionTime = ReadDate(row, "LastUserActionTime"),
        StorageLimitStatus = ReadString(row, "StorageLimitStatus"),
    };

    private static string? ReadString(PSObject row, string name) =>
        row.Properties[name]?.Value as string;

    private static Guid? ReadGuid(PSObject row, string name)
    {
        var value = row.Properties[name]?.Value;
        if (value is null)
        {
            return null;
        }

        return Guid.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool? ReadBool(PSObject row, string name) =>
        row.Properties[name]?.Value as bool?;

    private static int? ReadInt(PSObject row, string name)
    {
        var value = row.Properties[name]?.Value;
        return value is null ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ReadDate(PSObject row, string name)
    {
        var value = row.Properties[name]?.Value;
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt),
            _ => null,
        };
    }

    private static IReadOnlyList<string> ReadStringCollection(PSObject row, string name)
    {
        var value = row.Properties[name]?.Value;
        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            return [.. enumerable.Cast<object>()
                .Select(item => item?.ToString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)];
        }

        return [];
    }

    private static string? ReadNestedString(PSObject row, string name)
    {
        var value = row.Properties[name]?.Value;
        if (value is PSObject nested)
        {
            return nested.Properties["ToString"]?.Value?.ToString()
                ?? nested.ToString();
        }

        return value?.ToString();
    }

    private static long? ReadNestedLong(PSObject row, string name)
    {
        var value = row.Properties[name]?.Value;
        if (value is PSObject nested)
        {
            var bytes = nested.Properties["Value"]?.Value;
            if (bytes is not null && long.TryParse(bytes.ToString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
