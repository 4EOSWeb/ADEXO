using System.Collections.ObjectModel;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Mailbox;

namespace ADEXO.Desktop;

/// <summary>
/// Projects the read-only models into the seven product sections.
/// <para>
/// Every projection is independent: a missing cloud identity leaves the mailbox section empty without
/// touching the Active Directory section, and a missing mailbox does not erase the licence view. The
/// projections also carry the wording the UI shows, so a security-relevant state such as external
/// forwarding is stated in one place rather than re-derived per view.
/// </para>
/// </summary>
public static class ProductSections
{
    private const string NoAdUser = "Load a user from Active Directory Users and Computers to begin.";

    public static void BuildOverview(
        DetailSectionViewModel section,
        VerifiedDirectoryUser? adUser,
        CloudDirectoryUser? cloudUser,
        Contracts.Mailbox.MailboxDetails? mailbox,
        string cloudCorrelationState)
    {
        if (adUser is null)
        {
            section.SetEmpty(NoAdUser);
            return;
        }

        var identity = new DetailGroup { Title = "Identity" };
        identity.Rows.Add(DetailProjection.Row("Display name", adUser.DisplayName));
        identity.Rows.Add(DetailProjection.Row("sAMAccountName", adUser.SamAccountName));
        identity.Rows.Add(DetailProjection.Row("UPN", adUser.UserPrincipalName));
        identity.Rows.Add(DetailProjection.Row("AD status", "Loaded and verified", DetailSeverity.Good));
        identity.Rows.Add(DetailProjection.Row("objectGUID", adUser.ObjectGuid.ToString("D")));

        var cloud = new DetailGroup { Title = "Microsoft 365" };
        cloud.Rows.Add(DetailProjection.Row("Cloud correlation", cloudCorrelationState,
            cloudUser is null ? DetailSeverity.Muted : DetailSeverity.Good));
        cloud.Rows.Add(DetailProjection.Row("Entra object ID", cloudUser?.EntraObjectId.ToString("D")));

        var licences = cloudUser is null || cloudUser.Licenses.Count == 0
            ? "—"
            : string.Join(", ", cloudUser.Licenses.Select(l => l.Label));
        cloud.Rows.Add(DetailProjection.Row("Licences", licences));

        if (cloudUser is not null && !cloudUser.LicenseCatalogueResolved && cloudUser.Licenses.Count > 0)
        {
            cloud.Rows.Add(DetailProjection.Row("Licence names", "Tenant catalogue unavailable; identifiers shown",
                DetailSeverity.Muted));
        }

        var mail = new DetailGroup { Title = "Mailbox" };
        mail.Rows.Add(DetailProjection.Row("Mailbox state", MailboxStateText(mailbox),
            mailbox?.Kind == MailboxKind.NoMailbox ? DetailSeverity.Muted : DetailSeverity.Normal));
        mail.Rows.Add(DetailProjection.Row("Primary SMTP", mailbox?.PrimarySmtpAddress));
        mail.Rows.Add(ForwardingRow(mailbox));
        mail.Rows.Add(DetailProjection.Row("Archive",
            mailbox?.Archive switch
            {
                ArchiveState.Enabled => "Enabled",
                ArchiveState.Disabled => "Disabled",
                _ => "Unavailable",
            }));

        section.Populate(identity, cloud, mail);
    }

    public static void BuildActiveDirectory(DetailSectionViewModel section, VerifiedDirectoryUser? adUser)
    {
        if (adUser is null)
        {
            section.SetEmpty(NoAdUser);
            return;
        }

        var identity = new DetailGroup { Title = "Identity" };
        identity.Rows.Add(DetailProjection.Row("Distinguished name", adUser.DistinguishedName));
        identity.Rows.Add(DetailProjection.Row("Canonical name", adUser.CanonicalName));
        identity.Rows.Add(DetailProjection.Row("Object class", adUser.ObjectClass));
        identity.Rows.Add(DetailProjection.Row("objectGUID", adUser.ObjectGuid.ToString("D")));
        identity.Rows.Add(DetailProjection.Row("Object SID", adUser.ObjectSid));

        var account = new DetailGroup { Title = "Account" };
        account.Rows.Add(DetailProjection.Row("Enabled state", adUser.Enabled.ToString()));
        account.Rows.Add(DetailProjection.Row("Account expiration", adUser.ExpirationKind.ToString()));
        account.Rows.Add(DetailProjection.Row("Password last set", adUser.PasswordLastSetKind.ToString()));
        account.Rows.Add(DetailProjection.Row("Lockout (this replica only)", adUser.Lockout.ToString()));
        account.Rows.Add(DetailProjection.Row("Created", DetailProjection.Date(adUser.WhenCreatedUtc)));
        account.Rows.Add(DetailProjection.Row("Changed", DetailProjection.Date(adUser.WhenChangedUtc)));

        var contact = new DetailGroup { Title = "Contact" };
        contact.Rows.Add(DetailProjection.Row("Email", adUser.Mail));
        contact.Rows.Add(DetailProjection.Row("Phone", adUser.TelephoneNumber));
        contact.Rows.Add(DetailProjection.Row("Mobile", adUser.Mobile));
        contact.Rows.Add(DetailProjection.Row("Office", adUser.Office));

        var org = new DetailGroup { Title = "Organization" };
        org.Rows.Add(DetailProjection.Row("Title", adUser.Title));
        org.Rows.Add(DetailProjection.Row("Department", adUser.Department));
        org.Rows.Add(DetailProjection.Row("Company", adUser.Company));
        org.Rows.Add(DetailProjection.Row("Manager", adUser.ManagerDisplayName));

        section.Populate(identity, account, contact, org);
    }

    public static void BuildMicrosoft365(
        DetailSectionViewModel section,
        bool authenticated,
        Guid? tenantId,
        string? signedInAs,
        string correlationState,
        CloudDirectoryUser? cloudUser)
    {
        var auth = new DetailGroup { Title = "Authentication" };
        auth.Rows.Add(DetailProjection.Row("Status", authenticated ? "Connected" : "Disconnected",
            authenticated ? DetailSeverity.Good : DetailSeverity.Muted));
        auth.Rows.Add(DetailProjection.Row("Signed in as", signedInAs));
        auth.Rows.Add(DetailProjection.Row("Tenant", tenantId?.ToString("D")));

        if (cloudUser is null)
        {
            var state = new DetailGroup { Title = "Cloud identity" };
            state.Rows.Add(DetailProjection.Row("Correlation", correlationState, DetailSeverity.Muted));
            section.Populate(auth, state);
            return;
        }

        var identity = new DetailGroup { Title = "Cloud identity" };
        identity.Rows.Add(DetailProjection.Row("Correlation", correlationState, DetailSeverity.Good));
        identity.Rows.Add(DetailProjection.Row("Entra object ID", cloudUser.EntraObjectId.ToString("D")));
        identity.Rows.Add(DetailProjection.Row("Cloud UPN", cloudUser.UserPrincipalName));
        identity.Rows.Add(DetailProjection.Row("Mail", cloudUser.Mail));
        identity.Rows.Add(DetailProjection.Row("Account enabled", cloudUser.AccountEnabled is null ? "—" : cloudUser.AccountEnabled.Value ? "Yes" : "No"));
        identity.Rows.Add(DetailProjection.Row("Usage location", cloudUser.UsageLocation));

        var sync = new DetailGroup { Title = "Synchronization" };
        sync.Rows.Add(DetailProjection.Row("Sync enabled", cloudUser.OnPremisesSyncEnabled is null ? "Not synchronized" : cloudUser.OnPremisesSyncEnabled.Value ? "Synchronized from Active Directory" : "Cloud only"));
        sync.Rows.Add(DetailProjection.Row("Last sync", DetailProjection.Date(cloudUser.OnPremisesLastSyncDateTime)));
        sync.Rows.Add(DetailProjection.Row("On-premises DN", cloudUser.OnPremisesDistinguishedName));

        var licenses = new DetailGroup { Title = "Licences" };
        if (cloudUser.Licenses.Count == 0)
        {
            licenses.Rows.Add(DetailProjection.Row("Assigned", "No licences assigned", DetailSeverity.Muted));
        }
        else
        {
            foreach (var license in cloudUser.Licenses)
            {
                licenses.Rows.Add(DetailProjection.Row(
                    license.Label,
                    license.IsUnresolved ? "Tenant catalogue did not resolve this SKU" : "Assigned",
                    license.IsUnresolved ? DetailSeverity.Muted : DetailSeverity.Normal));
            }

            if (!cloudUser.LicenseCatalogueResolved)
            {
                licenses.Rows.Add(DetailProjection.Row("Licence names", "Tenant catalogue unavailable; identifiers shown", DetailSeverity.Muted));
            }
        }

        section.Populate(auth, identity, sync, licenses);
    }

    public static void BuildMailbox(DetailSectionViewModel section, Contracts.Mailbox.MailboxDetails? mailbox, MailboxUsage? usage)
    {
        if (mailbox is null || mailbox.Kind == MailboxKind.NotEvaluated)
        {
            section.SetEmpty("Connect to Exchange Online to read mailbox state.");
            return;
        }

        if (mailbox.Kind == MailboxKind.NoMailbox)
        {
            section.SetEmpty("This user has no Exchange Online mailbox.");
            return;
        }

        if (mailbox.Kind == MailboxKind.MailboxIdentityConflict)
        {
            section.SetEmpty("The mailbox Exchange returned does not match this user. ADEXO will not display it.");
            return;
        }

        var general = new DetailGroup { Title = "Mailbox" };
        general.Rows.Add(DetailProjection.Row("Type", MailboxKindText(mailbox.Kind)));
        general.Rows.Add(DetailProjection.Row("Alias", mailbox.Alias));
        general.Rows.Add(DetailProjection.Row("Primary SMTP", mailbox.PrimarySmtpAddress));
        general.Rows.Add(DetailProjection.Boolean("Hidden from address lists", mailbox.HiddenFromAddressListsEnabled));
        general.Rows.Add(DetailProjection.Row("Exchange GUID", mailbox.ExchangeGuid?.ToString("D")));

        var archive = new DetailGroup { Title = "Archive" };
        archive.Rows.Add(DetailProjection.Row("State", mailbox.Archive switch
        {
            ArchiveState.Enabled => "Enabled",
            ArchiveState.Disabled => "Disabled",
            _ => "Unavailable",
        }));
        archive.Rows.Add(DetailProjection.Row("Archive GUID", mailbox.ArchiveGuid?.ToString("D")));

        var forwarding = new DetailGroup { Title = "Forwarding" };
        forwarding.Rows.Add(ForwardingRow(mailbox));
        if (!string.IsNullOrWhiteSpace(mailbox.ForwardingSmtpAddress))
        {
            forwarding.Footnote("Mail is forwarded outside the organization.");
        }

        var sent = new DetailGroup { Title = "Sent Items behaviour" };
        sent.Rows.Add(DetailProjection.Boolean("Copy on Send As", mailbox.MessageCopyForSentAsEnabled));
        sent.Rows.Add(DetailProjection.Boolean("Copy on Send on Behalf", mailbox.MessageCopyForSendOnBehalfEnabled));

        var usageGroup = new DetailGroup { Title = "Usage" };
        if (usage is null || usage.ItemCount is null && usage.TotalItemSizeText is null)
        {
            usageGroup.Rows.Add(DetailProjection.Row("Statistics", "Statistics unavailable", DetailSeverity.Muted));
        }
        else
        {
            usageGroup.Rows.Add(DetailProjection.Row("Total size", usage.TotalItemSizeText));
            usageGroup.Rows.Add(DetailProjection.Row("Items", usage.ItemCount?.ToString()));
            usageGroup.Rows.Add(DetailProjection.Row("Deleted items", usage.DeletedItemCount?.ToString()));
            usageGroup.Rows.Add(DetailProjection.Row("Last logon", DetailProjection.Date(usage.LastLogonTime)));
            usageGroup.Rows.Add(DetailProjection.Row("Last user action", DetailProjection.Date(usage.LastUserActionTime)));
            usageGroup.Footnote("Mailbox operation metadata, not a record of working hours or productivity.");
        }

        section.Populate(general, archive, forwarding, sent, usageGroup);
    }

    public static void BuildEmailAddresses(DetailSectionViewModel section, Contracts.Mailbox.MailboxDetails? mailbox)
    {
        var addresses = mailbox?.EmailAddresses ?? [];
        if (addresses.Count == 0)
        {
            section.SetEmpty("No Exchange email addresses are available. Connect to Exchange Online, or the user has no mailbox.");
            return;
        }

        var primary = new DetailGroup { Title = "Primary" };
        var primaryAddress = addresses.FirstOrDefault(a => a.StartsWith("SMTP:", StringComparison.Ordinal));
        primary.Rows.Add(DetailProjection.Row("Primary SMTP", primaryAddress?.Substring(5) ?? mailbox?.PrimarySmtpAddress));

        var smtp = new DetailGroup { Title = "SMTP aliases" };
        foreach (var address in addresses.Where(a => a.StartsWith("smtp:", StringComparison.Ordinal)))
        {
            smtp.Rows.Add(DetailProjection.Row("smtp", address.Substring(5)));
        }

        var sip = new DetailGroup { Title = "SIP" };
        foreach (var address in addresses.Where(a => a.StartsWith("sip:", StringComparison.OrdinalIgnoreCase)))
        {
            sip.Rows.Add(DetailProjection.Row("sip", address.Substring(4)));
        }

        var x500 = new DetailGroup { Title = "X500" };
        foreach (var address in addresses.Where(a => a.StartsWith("X500:", StringComparison.OrdinalIgnoreCase)))
        {
            x500.Rows.Add(DetailProjection.Row("x500", address.Substring(5)));
        }

        var other = new DetailGroup { Title = "Other" };
        var classified = new[] { "SMTP:", "smtp:", "sip:", "X500:" };
        foreach (var address in addresses.Where(a => !classified.Any(p => a.StartsWith(p, StringComparison.OrdinalIgnoreCase))))
        {
            other.Rows.Add(DetailProjection.Row("address", address));
        }

        section.Populate(primary, smtp, sip, x500, other);
    }

    public static void BuildDelegation(DetailSectionViewModel section, DelegationReadResult? delegation)
    {
        if (delegation is null || delegation.State == DelegationState.NotEvaluated)
        {
            section.SetEmpty("Connect to Exchange Online to read delegation.");
            return;
        }

        if (delegation.State == DelegationState.Unavailable)
        {
            section.SetEmpty(delegation.Reason ?? "Delegation is unavailable.");
            return;
        }

        var full = new DetailGroup { Title = "Full Access" };
        foreach (var entry in delegation.FullAccess)
        {
            full.Rows.Add(DetailProjection.Row(entry.DisplayName ?? entry.Trustee, entry.IsResolved ? "Granted" : "Unresolved trustee",
                entry.IsResolved ? DetailSeverity.Normal : DetailSeverity.Warning));
        }

        if (full.Rows.Count == 0)
        {
            full.Rows.Add(DetailProjection.Row("None", "No Full Access grants", DetailSeverity.Muted));
        }

        var sendAs = new DetailGroup { Title = "Send As" };
        foreach (var entry in delegation.SendAs)
        {
            sendAs.Rows.Add(DetailProjection.Row(entry.DisplayName ?? entry.Trustee, "Granted"));
        }

        if (sendAs.Rows.Count == 0)
        {
            sendAs.Rows.Add(DetailProjection.Row("None", "No Send As grants", DetailSeverity.Muted));
        }

        var sendOnBehalf = new DetailGroup { Title = "Send on Behalf" };
        foreach (var entry in delegation.SendOnBehalf)
        {
            sendOnBehalf.Rows.Add(DetailProjection.Row(entry.DisplayName ?? entry.Trustee, "Granted"));
        }

        if (sendOnBehalf.Rows.Count == 0)
        {
            sendOnBehalf.Rows.Add(DetailProjection.Row("None", "No Send on Behalf grants", DetailSeverity.Muted));
        }

        section.Populate(full, sendAs, sendOnBehalf);
    }

    private static DetailRow ForwardingRow(MailboxDetails? mailbox) => mailbox?.Forwarding switch
    {
        ForwardingKind.ExternalForwarding =>
            DetailProjection.Row("Forwarding", $"External → {mailbox.ForwardingSmtpAddress}", DetailSeverity.Warning),
        ForwardingKind.InternalForwarding =>
            DetailProjection.Row("Forwarding", $"Internal → {mailbox.ForwardingAddress}"),
        ForwardingKind.DeliverAndForward =>
            DetailProjection.Row("Forwarding", $"Deliver and forward → {mailbox.ForwardingAddress}"),
        ForwardingKind.None => DetailProjection.Row("Forwarding", "No forwarding", DetailSeverity.Muted),
        _ => DetailProjection.Row("Forwarding", "Unavailable", DetailSeverity.Muted),
    };

    private static string MailboxStateText(MailboxDetails? mailbox) => mailbox?.Kind switch
    {
        MailboxKind.UserMailbox => "User mailbox",
        MailboxKind.SharedMailbox => "Shared mailbox",
        MailboxKind.RoomMailbox => "Room mailbox",
        MailboxKind.EquipmentMailbox => "Equipment mailbox",
        MailboxKind.NoMailbox => "No mailbox",
        MailboxKind.UnsupportedRecipient => "Unsupported recipient",
        MailboxKind.MailboxIdentityConflict => "Identity conflict",
        _ => "Not read",
    };

    private static string MailboxKindText(MailboxKind kind) => kind switch
    {
        MailboxKind.UserMailbox => "User mailbox",
        MailboxKind.SharedMailbox => "Shared mailbox",
        MailboxKind.RoomMailbox => "Room mailbox",
        MailboxKind.EquipmentMailbox => "Equipment mailbox",
        _ => "Unknown",
    };
}

internal static class DetailGroupExtensions
{
    /// <summary>Attaches a footnote and returns the same group, so it reads as a fluent call.</summary>
    public static DetailGroup Footnote(this DetailGroup group, string text)
    {
        group.Footnote = text;
        return group;
    }
}
