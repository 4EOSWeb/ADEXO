using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Configuration;
using ADEXO.Graph.Authentication;

namespace ADEXO.Graph.Tests;

public sealed class MicrosoftIdentityConfigurationTests
{
    [Fact]
    public void Absent_identifiers_report_not_configured_rather_than_throwing()
    {
        var created = MicrosoftIdentityConfiguration.TryCreate(null, out var configuration, out var reason);

        Assert.False(created);
        Assert.Null(configuration);
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData(null, "c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af")]
    [InlineData("c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af", null)]
    [InlineData("", "")]
    public void A_missing_half_of_the_pair_is_not_configured(string? tenant, string? client)
    {
        var created = MicrosoftIdentityConfiguration.TryCreate(
            new MicrosoftGraphOptions { TenantId = tenant, ClientId = client },
            out _,
            out var reason);

        Assert.False(created);
        Assert.NotNull(reason);
    }

    [Fact]
    public void A_malformed_tenant_identifier_is_rejected()
    {
        var created = MicrosoftIdentityConfiguration.TryCreate(
            new MicrosoftGraphOptions { TenantId = "not-a-guid", ClientId = "22222222-3333-4444-5555-666666666666" },
            out _,
            out var reason);

        Assert.False(created);
        Assert.Contains("tenant", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_malformed_client_identifier_is_rejected()
    {
        var created = MicrosoftIdentityConfiguration.TryCreate(
            new MicrosoftGraphOptions { TenantId = "c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af", ClientId = "not-a-guid" },
            out _,
            out var reason);

        Assert.False(created);
        Assert.Contains("client", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_valid_pair_builds_a_single_tenant_authority_and_broker_redirect()
    {
        const string tenant = "c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af";
        const string client = "22222222-3333-4444-5555-666666666666";

        var created = MicrosoftIdentityConfiguration.TryCreate(
            new MicrosoftGraphOptions { TenantId = tenant, ClientId = client },
            out var configuration,
            out _);

        Assert.True(created);
        Assert.NotNull(configuration);
        Assert.Equal(Guid.Parse(tenant), configuration.TenantId);
        Assert.Equal($"https://login.microsoftonline.com/{tenant}", configuration.Authority);

        // The broker redirect is a registration value. It is recorded so the portal step is exact,
        // but MSAL does not receive it: Microsoft documents that WAM needs no redirect URI in MSAL.
        Assert.Equal(
            $"ms-appx-web://Microsoft.AAD.BrokerPlugin/{client}",
            configuration.RegistrationRedirectUri);
    }

    [Fact]
    public void The_configuration_exposes_no_msal_redirect_parameter()
    {
        // A property that reads as "the value to pass to WithRedirectUri" is exactly the mistake this
        // correction removes, so the type must not offer one.
        var names = typeof(MicrosoftIdentityConfiguration).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain("RedirectUri", names);
        Assert.Contains("RegistrationRedirectUri", names);
    }

    [Fact]
    public void The_requested_scopes_are_read_only_and_delegated()
    {
        Assert.Equal(
            ["User.Read", "User.Read.All", "Organization.Read.All"],
            MicrosoftIdentityConfiguration.Scopes);
    }

    [Fact]
    public void No_write_or_mail_scope_is_requested()
    {
        string[] forbidden =
        [
            "Mail.Read", "Mail.ReadBasic", "Mail.ReadWrite", "Mail.Send",
            "User.ReadWrite.All", "Directory.ReadWrite.All", "Directory.Read.All",
            "LicenseAssignment.ReadWrite.All", "User.EnableDisableAccount.All",
            "Group.ReadWrite.All", "RoleManagement.ReadWrite.Directory",
        ];

        foreach (var scope in forbidden)
        {
            Assert.DoesNotContain(scope, MicrosoftIdentityConfiguration.Scopes);
        }
    }

    [Fact]
    public void The_configuration_type_exposes_no_secret_member()
    {
        // The same guard the application applies to configuration types at startup.
        ConfigurationGuard.EnsureNoSecretProperties(
            typeof(MicrosoftGraphOptions),
            typeof(MicrosoftIdentityConfiguration));
    }
}
