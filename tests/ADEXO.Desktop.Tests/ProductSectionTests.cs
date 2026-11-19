using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Mailbox;

namespace ADEXO.Desktop.Tests;

/// <summary>
/// Proves each section renders the right content, and that one provider being unavailable never blanks
/// another section.
/// </summary>
public sealed class ProductSectionTests
{
    private static VerifiedDirectoryUser AdUser() => new()
    {
        ObjectGuid = Guid.Parse("a116a060-4fc6-482b-be01-494c2cf4606e"),
        DistinguishedName = "CN=User,OU=Lab,DC=example,DC=com",
        CanonicalName = "example.com/Lab/User",
        ObjectClass = "user",
        DisplayName = "Example User",
        SamAccountName = "user",
        UserPrincipalName = "user@example.test",
        ObjectSid = "S-1-5-21-1-2-3-1103",
        Stage = IdentityStage.DisplayDataLoaded,
    };

    private static CloudDirectoryUser CloudUser(params Guid[] skuIds) => new()
    {
        EntraObjectId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        DisplayName = "Example User",
        UserPrincipalName = "user@example.test",
        UsageLocation = "US",
        OnPremisesSyncEnabled = true,
        Licenses = skuIds.Select(id => new CloudLicenseAssignment(id, "SKU_PART", "Tenant SKU name", null)).ToList(),
        LicenseCatalogueResolved = true,
    };

    private static MailboxDetails Mailbox(
        MailboxKind kind = MailboxKind.UserMailbox,
        ForwardingKind forwarding = ForwardingKind.None,
        ArchiveState archive = ArchiveState.Disabled,
        IReadOnlyList<string>? addresses = null) => new()
        {
            Kind = kind,
            ExternalDirectoryObjectId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            PrimarySmtpAddress = "user@example.test",
            Alias = "user",
            Forwarding = forwarding,
            ForwardingSmtpAddress = forwarding == ForwardingKind.ExternalForwarding ? "smtp:outside@elsewhere.test" : null,
            ForwardingAddress = forwarding == ForwardingKind.InternalForwarding ? "cn=other" : null,
            Archive = archive,
            EmailAddresses = addresses ?? ["SMTP:user@example.test", "smtp:alias@example.test"],
        };

    [Fact]
    public void Overview_summarizes_ad_cloud_and_mailbox()
    {
        var section = new DetailSectionViewModel("Overview");

        ProductSections.BuildOverview(section, AdUser(), CloudUser(Guid.NewGuid()), Mailbox(), "Verified");

        Assert.True(section.HasContent);
        var all = section.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Contains(all, r => r.Label == "Cloud correlation" && r.Value == "Verified");
        Assert.Contains(all, r => r.Label == "Primary SMTP" && r.Value == "user@example.test");
        Assert.Contains(all, r => r.Label == "objectGUID");
    }

    [Fact]
    public void Overview_without_a_cloud_user_still_shows_ad()
    {
        var section = new DetailSectionViewModel("Overview");

        ProductSections.BuildOverview(section, AdUser(), null, null, "Unmatched");

        var all = section.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Contains(all, r => r.Label == "AD status");
        Assert.Contains(all, r => r.Label == "Cloud correlation" && r.Value == "Unmatched");
        Assert.Contains(all, r => r.Label == "Mailbox state" && r.Value == "Not read");
    }

    [Fact]
    public void An_empty_overview_explains_what_to_do()
    {
        var section = new DetailSectionViewModel("Overview");

        ProductSections.BuildOverview(section, null, null, null, "Not evaluated");

        Assert.False(section.HasContent);
        Assert.Contains("Active Directory Users and Computers", section.EmptyMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Microsoft_365_reports_connection_state_without_a_cloud_user()
    {
        var section = new DetailSectionViewModel("Microsoft 365");

        ProductSections.BuildMicrosoft365(section, authenticated: false, tenantId: null, signedInAs: null,
            correlationState: "Not signed in", cloudUser: null);

        var all = section.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Contains(all, r => r.Label == "Status" && r.Value == "Disconnected");
        Assert.Contains(all, r => r.Label == "Correlation" && r.Value == "Not signed in");
    }

    [Fact]
    public void Licences_are_listed_and_a_missing_catalogue_is_stated()
    {
        var section = new DetailSectionViewModel("Microsoft 365");
        var user = new CloudDirectoryUser
        {
            EntraObjectId = Guid.NewGuid(),
            Licenses = [new CloudLicenseAssignment(Guid.NewGuid(), null, null, null)],
            LicenseCatalogueResolved = false,
        };

        ProductSections.BuildMicrosoft365(section, true, Guid.NewGuid(), "admin@lab.test", "Verified", user);

        var all = section.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Contains(all, r => r.Label == "Licence names" && r.Severity == DetailSeverity.Muted);
    }

    [Theory]
    [InlineData(MailboxKind.UserMailbox, "User mailbox")]
    [InlineData(MailboxKind.SharedMailbox, "Shared mailbox")]
    [InlineData(MailboxKind.RoomMailbox, "Room mailbox")]
    [InlineData(MailboxKind.EquipmentMailbox, "Equipment mailbox")]
    public void Mailbox_type_is_shown(MailboxKind kind, string expected)
    {
        var section = new DetailSectionViewModel("Mailbox");

        ProductSections.BuildMailbox(section, Mailbox(kind: kind), null);

        var all = section.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Contains(all, r => r.Label == "Type" && r.Value == expected);
    }

    [Fact]
    public void No_mailbox_shows_an_absence_not_an_error()
    {
        var section = new DetailSectionViewModel("Mailbox");

        ProductSections.BuildMailbox(section, new MailboxDetails { Kind = MailboxKind.NoMailbox }, null);

        Assert.False(section.HasContent);
        Assert.Contains("no Exchange Online mailbox", section.EmptyMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_identity_conflict_refuses_to_display_the_mailbox()
    {
        var section = new DetailSectionViewModel("Mailbox");

        ProductSections.BuildMailbox(section, new MailboxDetails { Kind = MailboxKind.MailboxIdentityConflict }, null);

        Assert.False(section.HasContent);
        Assert.Contains("does not match", section.EmptyMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void External_forwarding_is_visually_distinct()
    {
        var section = new DetailSectionViewModel("Mailbox");

        ProductSections.BuildMailbox(section, Mailbox(forwarding: ForwardingKind.ExternalForwarding), null);

        var row = section.Groups.SelectMany(g => g.Rows).Single(r => r.Label == "Forwarding");
        Assert.Equal(DetailSeverity.Warning, row.Severity);
        Assert.Contains("External", row.Value, StringComparison.Ordinal);

        var forwardingGroup = section.Groups.Single(g => g.Title == "Forwarding");
        Assert.NotNull(forwardingGroup.Footnote);
        Assert.Contains("outside the organization", forwardingGroup.Footnote!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ForwardingKind.None, "No forwarding")]
    [InlineData(ForwardingKind.InternalForwarding, "Internal")]
    [InlineData(ForwardingKind.DeliverAndForward, "Deliver and forward")]
    public void Forwarding_kinds_read_distinctly(ForwardingKind kind, string expected)
    {
        var section = new DetailSectionViewModel("Mailbox");

        ProductSections.BuildMailbox(section, Mailbox(forwarding: kind), null);

        var row = section.Groups.SelectMany(g => g.Rows).Single(r => r.Label == "Forwarding");
        Assert.Contains(expected, row.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_statistics_are_labelled_unavailable_not_an_error()
    {
        var section = new DetailSectionViewModel("Mailbox");

        ProductSections.BuildMailbox(section, Mailbox(), new MailboxUsage());

        var row = section.Groups.SelectMany(g => g.Rows).Single(r => r.Label == "Statistics");
        Assert.Equal("Statistics unavailable", row.Value);
    }

    [Fact]
    public void Usage_states_are_labelled_as_operation_metadata()
    {
        var section = new DetailSectionViewModel("Mailbox");

        ProductSections.BuildMailbox(section, Mailbox(), new MailboxUsage { ItemCount = 10, TotalItemSizeText = "1 MB" });

        var usage = section.Groups.Single(g => g.Title == "Usage");
        Assert.Contains("not a record of working hours", usage.Footnote!, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_addresses_are_grouped_by_scheme()
    {
        var section = new DetailSectionViewModel("Email addresses");
        var addresses = new[]
        {
            "SMTP:user@example.test",
            "smtp:alias@example.test",
            "sip:user@example.test",
            "X500:/o=org/ou=exchange/cn=user",
            "other:value",
        };

        ProductSections.BuildEmailAddresses(section, Mailbox(addresses: addresses));

        Assert.Contains(section.Groups, g => g.Title == "Primary" && g.Rows.Any(r => r.Value == "user@example.test"));
        Assert.Contains(section.Groups, g => g.Title == "SMTP aliases");
        Assert.Contains(section.Groups, g => g.Title == "SIP");
        Assert.Contains(section.Groups, g => g.Title == "X500");
        Assert.Contains(section.Groups, g => g.Title == "Other");
    }

    [Fact]
    public void Delegation_kinds_are_separated()
    {
        var section = new DetailSectionViewModel("Delegation");
        var delegation = new DelegationReadResult
        {
            State = DelegationState.Available,
            FullAccess = [new DelegationEntry { Kind = DelegationKind.FullAccess, Trustee = "a", IsResolved = true }],
            SendAs = [new DelegationEntry { Kind = DelegationKind.SendAs, Trustee = "b", IsResolved = true }],
            SendOnBehalf = [],
        };

        ProductSections.BuildDelegation(section, delegation);

        Assert.Contains(section.Groups, g => g.Title == "Full Access" && g.Rows.Count == 1);
        Assert.Contains(section.Groups, g => g.Title == "Send As" && g.Rows.Count == 1);
        Assert.Contains(section.Groups, g => g.Title == "Send on Behalf" && g.Rows.Any(r => r.Value.Contains("No Send on Behalf")));
    }

    [Fact]
    public void An_unresolved_trustee_is_flagged()
    {
        var section = new DetailSectionViewModel("Delegation");
        var delegation = new DelegationReadResult
        {
            State = DelegationState.Available,
            FullAccess = [new DelegationEntry { Kind = DelegationKind.FullAccess, Trustee = "S-1-5-21-orphan", IsResolved = false }],
        };

        ProductSections.BuildDelegation(section, delegation);

        var row = section.Groups.Single(g => g.Title == "Full Access").Rows.Single();
        Assert.Equal(DetailSeverity.Warning, row.Severity);
    }

    [Fact]
    public void Delegation_absent_when_exchange_is_not_connected()
    {
        var section = new DetailSectionViewModel("Delegation");

        ProductSections.BuildDelegation(section, null);

        Assert.False(section.HasContent);
        Assert.Contains("Connect to Exchange Online", section.EmptyMessage, StringComparison.Ordinal);
    }
}
