using ADEXO.ExchangeOnline;

namespace ADEXO.ExchangeOnline.Tests;

/// <summary>
/// The allowlist must name cmdlets the installed ExchangeOnlineManagement module actually exports.
/// <para>
/// A live check found <c>Get-MailboxPermission</c> and <c>Get-RecipientPermission</c> are not exported by
/// version 3.11.1 on this platform; only the EXO-prefixed forms are. Allowlisting the legacy names
/// produced a delegation read that could never succeed, and the failure would have surfaced as a
/// permission error rather than as the naming mistake it was.
/// </para>
/// </summary>
public sealed class ExchangeCommandAvailabilityTests
{
    [Fact]
    public void The_permission_reads_use_the_exo_prefixed_cmdlets()
    {
        Assert.Equal("Get-EXOMailboxPermission", ExchangeReadCommands.GetMailboxPermission);
        Assert.Equal("Get-EXORecipientPermission", ExchangeReadCommands.GetRecipientPermission);
    }

    [Fact]
    public void No_allowlisted_command_uses_a_legacy_unprefixed_permission_name()
    {
        // Get-Mailbox and Get-MailboxPermission are the Exchange on-premises / remoting names. The module
        // hosts EXO cmdlets, and mixing the two namespaces is how an invocation fails at runtime only.
        string[] legacy =
        [
            "Get-Mailbox",
            "Get-MailboxPermission",
            "Get-RecipientPermission",
            "Get-MailboxStatistics",
        ];

        foreach (var command in ExchangeReadCommands.Allowed)
        {
            Assert.DoesNotContain(command, legacy);
        }
    }

    [Fact]
    public void Every_allowlisted_read_is_a_get_command()
    {
        foreach (var command in ExchangeReadCommands.ReadsOnly)
        {
            Assert.StartsWith("Get-", command, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_allowlist_contains_no_write_permission_cmdlet()
    {
        string[] writes =
        [
            "Set-Mailbox",
            "Add-MailboxPermission",
            "Remove-MailboxPermission",
            "Add-RecipientPermission",
            "Remove-RecipientPermission",
            "Set-MailboxAutoReplyConfiguration",
        ];

        foreach (var command in ExchangeReadCommands.Allowed)
        {
            Assert.DoesNotContain(command, writes, StringComparer.OrdinalIgnoreCase);
        }
    }
}
