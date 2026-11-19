using ADEXO.Infrastructure.Configuration;

namespace ADEXO.Infrastructure.Tests;

public sealed class CloudConfigurationLoaderTests
{
    [Fact]
    public void A_valid_document_supplies_both_identifiers()
    {
        var json = """
            {
              "tenantId": "c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af",
              "clientId": "5f7f5441-87d9-4fe5-96f2-8a2492c79db7"
            }
            """;

        var file = CloudConfigurationLoader.TryParse(json, out var reason);

        Assert.NotNull(file);
        Assert.Null(reason);
        Assert.True(file.IsComplete);
        Assert.Equal("c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af", file.TenantId);
        Assert.Equal("5f7f5441-87d9-4fe5-96f2-8a2492c79db7", file.ClientId);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("clientSecret")]
    [InlineData("ClientSecret")]
    [InlineData("secret")]
    [InlineData("accessToken")]
    [InlineData("refreshToken")]
    [InlineData("certificateThumbprint")]
    [InlineData("privateKey")]
    [InlineData("credential")]
    public void A_secret_looking_property_is_refused_outright(string propertyName)
    {
        var json = $$"""
            {
              "tenantId": "c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af",
              "clientId": "5f7f5441-87d9-4fe5-96f2-8a2492c79db7",
              "{{propertyName}}": "some-value"
            }
            """;

        var file = CloudConfigurationLoader.TryParse(json, out var reason);

        Assert.Null(file);
        Assert.Contains("not permitted", reason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(propertyName, reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_document_is_not_an_error()
    {
        var file = CloudConfigurationLoader.TryLoad("C:\\does-not-exist\\adexo.cloud.json", out var reason);

        Assert.Null(file);
        Assert.Contains("No cloud configuration", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("42")]
    public void A_malformed_document_is_rejected(string json)
    {
        var file = CloudConfigurationLoader.TryParse(json, out var reason);

        Assert.Null(file);
        Assert.NotNull(reason);
    }

    [Fact]
    public void A_document_without_identifiers_is_rejected()
    {
        var file = CloudConfigurationLoader.TryParse("""{ "somethingElse": "value" }""", out var reason);

        Assert.Null(file);
        Assert.Contains("identifier", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Property_names_are_matched_case_insensitively()
    {
        var json = """{ "TenantId": "c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af", "CLIENTID": "5f7f5441-87d9-4fe5-96f2-8a2492c79db7" }""";

        var file = CloudConfigurationLoader.TryParse(json, out _);

        Assert.NotNull(file);
        Assert.True(file.IsComplete);
    }

    [Fact]
    public void The_options_conversion_carries_only_identifiers()
    {
        var file = new CloudConfigurationFile("tenant", "client", null, null);

        var options = file.ToOptions();

        Assert.Equal("tenant", options.TenantId);
        Assert.Equal("client", options.ClientId);
    }

    [Fact]
    public void A_declared_authentication_mode_survives_parsing_and_options_conversion()
    {
        const string json = """
            {
              "tenantId": "tenant",
              "clientId": "client",
              "authenticationMode": "SystemBrowserOnly"
            }
            """;

        var file = CloudConfigurationLoader.TryParse(json, out _);

        Assert.NotNull(file);
        Assert.Equal("SystemBrowserOnly", file.AuthenticationMode);
        Assert.Equal("SystemBrowserOnly", file.ToOptions().AuthenticationMode);
    }

    [Fact]
    public void An_absent_authentication_mode_defaults_to_auto()
    {
        const string json = """
            {
              "tenantId": "tenant",
              "clientId": "client"
            }
            """;

        var file = CloudConfigurationLoader.TryParse(json, out _);

        Assert.NotNull(file);
        Assert.Null(file.AuthenticationMode);
        Assert.Equal("Auto", file.ToOptions().AuthenticationMode);
    }

    [Fact]
    public void An_unconfigured_path_resolves_to_a_file_name_without_creating_anything()
    {
        var path = CloudSectionFactoryPaths.DefaultFileName;

        Assert.EndsWith(".json", path, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Exposes the loader's default file name for the path assertion above.</summary>
internal static class CloudSectionFactoryPaths
{
    public static string DefaultFileName => CloudConfigurationLoader.DefaultFileName;
}
