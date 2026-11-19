using System.Management.Automation;
using ADEXO.Contracts.Mailbox;
using ADEXO.Contracts.Results;

namespace ADEXO.ExchangeOnline.Tests;

public sealed class MailboxProviderTests
{
    private static readonly Guid EntraId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static PSObject MailboxRow(
        Guid? externalId = null,
        string recipientType = "UserMailbox",
        string? forwardingAddress = null,
        string? forwardingSmtp = null,
        bool? deliverAndForward = null,
        string? archiveStatus = null,
        string? primarySmtp = "user@example.test",
        string? alias = "user")
    {
        var o = new PSObject();
        o.Properties.Add(new PSNoteProperty("ExternalDirectoryObjectId", (externalId ?? EntraId).ToString()));
        o.Properties.Add(new PSNoteProperty("ExchangeGuid", Guid.NewGuid().ToString()));
        o.Properties.Add(new PSNoteProperty("RecipientTypeDetails", recipientType));
        o.Properties.Add(new PSNoteProperty("PrimarySmtpAddress", primarySmtp));
        o.Properties.Add(new PSNoteProperty("Alias", alias));
        o.Properties.Add(new PSNoteProperty("ForwardingAddress", forwardingAddress));
        o.Properties.Add(new PSNoteProperty("ForwardingSmtpAddress", forwardingSmtp));
        o.Properties.Add(new PSNoteProperty("DeliverToMailboxAndForward", deliverAndForward));
        o.Properties.Add(new PSNoteProperty("ArchiveStatus", archiveStatus));
        o.Properties.Add(new PSNoteProperty("HiddenFromAddressListsEnabled", false));
        o.Properties.Add(new PSNoteProperty("EmailAddresses", new[] { "SMTP:user@example.test", "smtp:user@example.onmicrosoft.com" }));
        return o;
    }

    private static ExchangeMailboxProvider Provider(RecordingExchangeInvoker invoker) =>
        new(invoker, new FixedClock());

    [Fact]
    public async Task A_user_mailbox_is_read_by_the_entra_object_identifier()
    {
        var invoker = new RecordingExchangeInvoker().Returns(ExchangeReadCommands.GetMailbox, MailboxRow());
        var provider = Provider(invoker);

        var result = await provider.GetMailboxAsync(EntraId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MailboxKind.UserMailbox, result.Value!.Kind);
        Assert.Equal(EntraId, result.Value.ExternalDirectoryObjectId);

        var call = invoker.Single(ExchangeReadCommands.GetMailbox);
        var external = Assert.Single(call.Parameters, p => p.Name == "ExternalDirectoryObjectId");
        Assert.Equal(EntraId, external.Value);

        // No mutable name may be used as the identity.
        Assert.DoesNotContain(call.Parameters, p => p.Name is "Identity" or "UserPrincipalName" or "PrimarySmtpAddress");
    }

    [Theory]
    [InlineData("UserMailbox", MailboxKind.UserMailbox)]
    [InlineData("SharedMailbox", MailboxKind.SharedMailbox)]
    [InlineData("RoomMailbox", MailboxKind.RoomMailbox)]
    [InlineData("EquipmentMailbox", MailboxKind.EquipmentMailbox)]
    [InlineData("MailContact", MailboxKind.UnsupportedRecipient)]
    public async Task Recipient_types_map_to_explicit_kinds(string recipientType, MailboxKind expected)
    {
        var invoker = new RecordingExchangeInvoker().Returns(ExchangeReadCommands.GetMailbox, MailboxRow(recipientType: recipientType));

        var result = await Provider(invoker).GetMailboxAsync(EntraId, CancellationToken.None);

        Assert.Equal(expected, result.Value!.Kind);
    }

    [Fact]
    public async Task No_mailbox_returns_unavailable_not_found()
    {
        var invoker = new RecordingExchangeInvoker();
        var result = await Provider(invoker).GetMailboxAsync(EntraId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MailboxKind.NoMailbox, result.Value!.Kind);
    }

    [Fact]
    public async Task A_different_object_returned_is_an_identity_conflict()
    {
        var other = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var invoker = new RecordingExchangeInvoker().Returns(ExchangeReadCommands.GetMailbox, MailboxRow(externalId: other));

        var result = await Provider(invoker).GetMailboxAsync(EntraId, CancellationToken.None);

        Assert.Equal(MailboxKind.MailboxIdentityConflict, result.Value!.Kind);
        Assert.Equal(other, result.Value.ExternalDirectoryObjectId);
    }

    [Fact]
    public async Task An_empty_identifier_is_rejected_before_any_call()
    {
        var invoker = new RecordingExchangeInvoker();
        var result = await Provider(invoker).GetMailboxAsync(Guid.Empty, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(invoker.Invocations);
    }

    [Theory]
    [InlineData(null, null, null, ForwardingKind.None)]
    [InlineData("cn=other", null, null, ForwardingKind.InternalForwarding)]
    [InlineData(null, "smtp:ext@outside.test", null, ForwardingKind.ExternalForwarding)]
    [InlineData("cn=other", null, true, ForwardingKind.DeliverAndForward)]
    public void Forwarding_is_classified_explicitly(
        string? address,
        string? smtp,
        bool? deliverAndForward,
        ForwardingKind expected)
    {
        Assert.Equal(expected, ExchangeMailboxProvider.ClassifyForwarding(address, smtp, deliverAndForward));
    }

    [Fact]
    public async Task External_forwarding_is_flagged_for_the_ui()
    {
        var invoker = new RecordingExchangeInvoker()
            .Returns(ExchangeReadCommands.GetMailbox, MailboxRow(forwardingSmtp: "smtp:ext@outside.test"));

        var result = await Provider(invoker).GetMailboxAsync(EntraId, CancellationToken.None);

        Assert.True(result.Value!.IsExternalForwarding);
        Assert.Equal(ForwardingKind.ExternalForwarding, result.Value.Forwarding);
    }

    [Fact]
    public async Task Archive_status_is_reported()
    {
        var enabled = new RecordingExchangeInvoker().Returns(ExchangeReadCommands.GetMailbox, MailboxRow(archiveStatus: "Active"));
        var disabled = new RecordingExchangeInvoker().Returns(ExchangeReadCommands.GetMailbox, MailboxRow(archiveStatus: null));

        Assert.Equal(ArchiveState.Enabled, (await Provider(enabled).GetMailboxAsync(EntraId, CancellationToken.None)).Value!.Archive);
        Assert.Equal(ArchiveState.Unavailable, (await Provider(disabled).GetMailboxAsync(EntraId, CancellationToken.None)).Value!.Archive);
    }

    [Fact]
    public async Task A_read_failure_is_reported_rather_than_treated_as_no_mailbox()
    {
        var invoker = new RecordingExchangeInvoker { Error = OperationError.Create(FailureCategory.Authorization, "missing role") };

        var result = await Provider(invoker).GetMailboxAsync(EntraId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCategory.Authorization, result.Error!.Category);
    }

    [Fact]
    public async Task Cancellation_is_reported_as_cancellation()
    {
        var invoker = new RecordingExchangeInvoker { Cancel = true };

        var result = await Provider(invoker).GetMailboxAsync(EntraId, CancellationToken.None);

        Assert.True(result.IsCanceled);
    }

    [Fact]
    public async Task Missing_statistics_do_not_become_a_missing_mailbox()
    {
        var invoker = new RecordingExchangeInvoker()
            .Returns(ExchangeReadCommands.GetMailbox, MailboxRow());
        var provider = Provider(invoker);

        var mailbox = (await provider.GetMailboxAsync(EntraId, CancellationToken.None)).Value!;
        var usage = await provider.GetUsageAsync(mailbox, CancellationToken.None);

        Assert.True(usage.IsSuccess);
        Assert.Equal(MailboxKind.UserMailbox, mailbox.Kind);
        Assert.Null(usage.Value!.ItemCount);
    }
}
