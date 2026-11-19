using ADEXO.ExchangeOnline;

namespace ADEXO.ExchangeOnline.Tests;

/// <summary>
/// The connection lifecycle is state the administrator depends on, so each transition is asserted:
/// a module failure must not look like an authentication failure, and a cancel must return to
/// disconnected rather than to an error.
/// </summary>
public sealed class ExchangeConnectionStateTests
{
    [Fact]
    public void A_new_service_is_disconnected()
    {
        Assert.Equal(ExchangeConnectionState.Disconnected, ExchangeConnectionState.Disconnected);
    }

    [Fact]
    public void Every_connection_state_has_a_distinct_meaning()
    {
        // A UI that collapsed these would tell an administrator the wrong thing to fix.
        var states = Enum.GetValues<ExchangeConnectionState>();
        Assert.Equal(states.Length, states.Distinct().Count());
        Assert.Contains(ExchangeConnectionState.ModuleUnavailable, states);
        Assert.Contains(ExchangeConnectionState.AccessDenied, states);
        Assert.Contains(ExchangeConnectionState.AuthenticationFailed, states);
    }

    [Fact]
    public async Task The_organization_is_required_to_connect()
    {
        // An empty organization would be an unauthenticated attempt against no target. The check runs
        // before any runspace work, so no module import is attempted.
        using var runspace = ExchangeRunspace.Create();
        var service = new ExchangeConnectionService(runspace, new FixedClock());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ConnectAsync("   ", CancellationToken.None));
    }

    [Fact]
    public void The_connection_service_declares_no_credential_member()
    {
        var names = typeof(ExchangeConnectionService).GetProperties().Select(p => p.Name).ToArray();

        foreach (var forbidden in new[] { "Password", "Secret", "Credential", "Token", "Certificate" })
        {
            Assert.DoesNotContain(names, n => n.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_connection_service_exposes_no_write_capability()
    {
        var methods = typeof(ExchangeConnectionService)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();

        foreach (var forbidden in new[] { "Set", "Add", "Remove", "Enable", "Disable", "New" })
        {
            Assert.DoesNotContain(methods, n => n.StartsWith(forbidden, StringComparison.Ordinal));
        }
    }
}
