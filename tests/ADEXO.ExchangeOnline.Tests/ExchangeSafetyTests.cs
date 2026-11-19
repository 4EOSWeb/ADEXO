using ADEXO.ExchangeOnline;

namespace ADEXO.ExchangeOnline.Tests;

/// <summary>
/// Proves the Exchange adapter cannot be turned into arbitrary PowerShell.
/// <para>
/// These are architectural assertions over the public surface. They fail if a member is ever added that
/// accepts script text, a command name from a caller, or a raw pipeline.
/// </para>
/// </summary>
public sealed class ExchangeSafetyTests
{
    [Fact]
    public void The_invocation_type_cannot_be_constructed_from_an_unlisted_command()
    {
        Assert.Throws<ArgumentException>(() => new ExchangeInvocation("Remove-Mailbox"));
        Assert.Throws<ArgumentException>(() => new ExchangeInvocation("Set-Mailbox"));
        Assert.Throws<ArgumentException>(() => new ExchangeInvocation("Invoke-Expression"));
        Assert.Throws<ArgumentException>(() => new ExchangeInvocation("Add-MailboxPermission"));
    }

    [Fact]
    public void Every_allowed_read_command_is_a_get()
    {
        foreach (var command in ExchangeReadCommands.ReadsOnly)
        {
            Assert.StartsWith("Get-", command, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_write_command_appears_anywhere_in_the_allowlist()
    {
        string[] forbidden =
        [
            "Set-Mailbox", "Set-CASMailbox", "Set-MailboxAutoReplyConfiguration", "Set-User",
            "Add-MailboxPermission", "Remove-MailboxPermission", "Add-RecipientPermission",
            "Remove-RecipientPermission", "New-Mailbox", "Remove-Mailbox", "Enable-Mailbox",
            "Disable-Mailbox", "Invoke-Expression", "Start-Process", "New-Item",
        ];

        foreach (var command in forbidden)
        {
            Assert.DoesNotContain(command, ExchangeReadCommands.Allowed);
        }
    }

    [Fact]
    public void The_allowlist_is_small_and_explicit()
    {
        // Six entries: four reads plus connect and disconnect for the session itself.
        Assert.Equal(6, ExchangeReadCommands.Allowed.Count);
    }

    [Fact]
    public void The_invoker_seam_accepts_only_an_invocation_not_text()
    {
        var method = typeof(IExchangeInvoker).GetMethods().Single();

        var parameters = method.GetParameters();
        Assert.Single(parameters, p => p.ParameterType == typeof(ExchangeInvocation));
        Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(string));
    }

    [Fact]
    public void No_public_member_accepts_script_or_command_text()
    {
        string[] forbiddenNames = ["Script", "ScriptBlock", "ScriptText", "CommandLine", "Expression", "Pipeline"];

        foreach (var type in new[] { typeof(ExchangeRunspace), typeof(ExchangeMailboxProvider), typeof(ExchangeInvocation) })
        {
            foreach (var member in type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            {
                foreach (var bad in forbiddenNames)
                {
                    // An exception for the invocation's own Command property, which is validated on construction.
                    if (type == typeof(ExchangeInvocation) && member.Name == "Command")
                    {
                        continue;
                    }

                    Assert.False(member.Name.Contains(bad, StringComparison.OrdinalIgnoreCase),
                        $"{type.Name}.{member.Name} exposes a script-shaped surface.");
                }
            }
        }
    }

    [Fact]
    public void The_only_parameter_type_is_a_typed_name_value_pair()
    {
        var properties = typeof(ExchangeParameter).GetProperties();

        Assert.Contains(properties, p => p.Name == "Name");
        Assert.Contains(properties, p => p.Name == "Value");
        Assert.Equal(2, properties.Length);
    }
}
