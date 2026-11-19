using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ADEXO.Contracts.Audit;
using ADEXO.Contracts.Authority;
using ADEXO.Contracts.Capabilities;
using ADEXO.Contracts.Concurrency;
using ADEXO.Contracts.Configuration;
using ADEXO.Contracts.Features;
using ADEXO.Contracts.Identity;
using ADEXO.Contracts.Operations;
using ADEXO.Contracts.Preview;
using ADEXO.Contracts.Secrets;
using Microsoft.Extensions.Logging;

namespace ADEXO.Contracts.Tests;

public sealed class SourceOfAuthorityTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SourceOfAuthority.ActiveDirectory, true)]
    [InlineData(SourceOfAuthority.MicrosoftEntra, true)]
    [InlineData(SourceOfAuthority.ExchangeOnline, false)]
    [InlineData(SourceOfAuthority.Unsupported, false)]
    [InlineData(SourceOfAuthority.Unknown, false)]
    public void Value_carries_source_and_editability(SourceOfAuthority source, bool editable)
    {
        var value = new AuthoritativeValue<string>(
            "liam@contoso.example",
            source,
            editable,
            editable ? null : "Another system owns this value.",
            Stamp);

        Assert.Equal(source, value.Source);
        Assert.Equal(editable, value.IsEditable);
        Assert.Equal(Stamp, value.LastRefreshedUtc);
    }

    [Fact]
    public void Read_only_value_requires_a_reason()
    {
        Assert.Throws<ArgumentException>(() => new AuthoritativeValue<string>("x", SourceOfAuthority.Calculated, false, " ", Stamp));
    }
}

public sealed class IdentityTests
{
    [Fact]
    public void Ad_identity_uses_object_guid_only()
    {
        var id = Guid.NewGuid();
        var left = new AdObjectIdentity(id, "CN=One,DC=example", "LDAP://CN=One,DC=example", "user", "S-1-5-21-1");
        var right = new AdObjectIdentity(id, "CN=Two,DC=example", "LDAP://CN=Two,DC=example", "user", null);

        Assert.Equal(left, right);
    }

    [Fact]
    public void Cloud_write_requires_a_verified_relationship()
    {
        var relationship = new CloudRelationship(Guid.NewGuid(), Guid.NewGuid(), false);
        Assert.Throws<InvalidOperationException>(() => CloudRelationship.EnsureWritable(relationship));
        Assert.Throws<InvalidOperationException>(() => CloudRelationship.EnsureWritable(null));
        CloudRelationship.EnsureWritable(new CloudRelationship(relationship.ObjectGuid, relationship.CloudObjectId, true));
    }
}

public sealed class FeatureIdTests
{
    [Fact]
    public void Known_features_are_unique_and_ordinal()
    {
        var names = KnownFeatureId.All.Select(feature => feature.Canonical).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("ExchangeOnline.Mailbox.Forwarding", names, StringComparer.Ordinal);
        Assert.Equal(
            KnownFeatureId.ExchangeOnlineMailboxForwarding,
            KnownFeatureId.All.Single(feature => string.Equals(feature.Canonical, "ExchangeOnline.Mailbox.Forwarding", StringComparison.Ordinal)));
        Assert.False(string.Equals(
            KnownFeatureId.EntraConnectSynchronizationDelta.Canonical,
            KnownFeatureId.EntraConnectSynchronizationDelta.Canonical.ToUpper(CultureInfo.GetCultureInfo("tr-TR")),
            StringComparison.Ordinal));
    }

    [Fact]
    public void External_feature_rejects_blank_names()
    {
        Assert.Throws<ArgumentException>(() => ExternalFeatureId.Create(" "));
        Assert.Equal(ExternalFeatureId.Create("Custom.Feature"), ExternalFeatureId.Create("Custom.Feature"));
    }
}

public sealed class ProviderCapabilityTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);
    private static readonly KnownFeatureId Feature = KnownFeatureId.ExchangeOnlineMailboxForwarding;

    [Fact]
    public void Factories_cover_the_valid_shapes()
    {
        AssertDisabled(ProviderCapability.Unavailable(ProviderKind.ExchangeOnline, Feature, SourceOfAuthority.ExchangeOnline, "Endpoint down.", Stamp));
        AssertDisabled(ProviderCapability.Unauthenticated(ProviderKind.ExchangeOnline, Feature, SourceOfAuthority.ExchangeOnline, "Sign-in required.", Stamp));
        var unauthorized = ProviderCapability.Unauthorized(ProviderKind.ExchangeOnline, Feature, SourceOfAuthority.ExchangeOnline, "Mailbox.ReadWrite", true, Stamp);
        Assert.False(unauthorized.CanWrite);
        Assert.Equal("Mailbox.ReadWrite", unauthorized.MissingPermission);
        var readOnly = ProviderCapability.ReadOnly(ProviderKind.ActiveDirectory, KnownFeatureId.ActiveDirectoryUserProxyAddresses, SourceOfAuthority.ActiveDirectory, "Edit proxyAddresses.", Stamp);
        Assert.True(readOnly.CanRead);
        Assert.False(readOnly.CanWrite);
        var readWrite = ProviderCapability.ReadWrite(ProviderKind.ExchangeOnline, Feature, SourceOfAuthority.ExchangeOnline, Stamp);
        Assert.True(readWrite.CanWrite);
        Assert.Null(readWrite.MissingPermission);
        AssertDisabled(ProviderCapability.Unsupported(ProviderKind.EntraConnect, KnownFeatureId.EntraConnectSynchronizationDelta, SourceOfAuthority.Unsupported, "Not configured.", Stamp));
    }

    [Theory]
    [InlineData(false, true, false, true, false, null, null)]
    [InlineData(false, false, false, false, true, null, "down")]
    [InlineData(true, false, true, false, false, null, "signed out")]
    [InlineData(true, true, false, true, true, "role", null)]
    [InlineData(true, true, true, true, true, null, "unsupported")]
    [InlineData(true, true, true, true, false, null, null)]
    [InlineData(true, true, true, false, false, "perm", null)]
    public void Contradictory_combinations_are_rejected(
        bool available,
        bool authenticated,
        bool authorized,
        bool canRead,
        bool canWrite,
        string? missing,
        string? reason)
    {
        var created = ProviderCapability.TryCreate(
            ProviderKind.ExchangeOnline,
            Feature,
            available,
            authenticated,
            authorized,
            canRead,
            canWrite,
            SourceOfAuthority.ExchangeOnline,
            missing,
            reason,
            Stamp,
            out _);

        Assert.False(created);
    }

    private static void AssertDisabled(ProviderCapability capability)
    {
        Assert.False(capability.CanWrite);
        Assert.False(string.IsNullOrWhiteSpace(capability.UnsupportedReason) && string.IsNullOrWhiteSpace(capability.MissingPermission));
    }
}

public sealed class ConcurrencyTokenTests
{
    [Fact]
    public void Tokens_match_only_the_same_kind_and_value()
    {
        var left = ConcurrencyToken.From(ConcurrencyTokenKind.GraphETag, "etag-1");
        var right = ConcurrencyToken.From(ConcurrencyTokenKind.GraphETag, "etag-1");
        var other = ConcurrencyToken.From(ConcurrencyTokenKind.GraphETag, "etag-2");
        var exchange = ConcurrencyToken.From(ConcurrencyTokenKind.ExchangeSnapshot, "etag-1");

        Assert.Equal(left, right);
        Assert.NotEqual(left, other);
        Assert.NotEqual(left, exchange);
        Assert.NotEqual(left, ConcurrencyToken.None);
    }
}

public sealed class SecretHandlingTests
{
    private const string Password = "test-password-never-log-8b4f";
    private const string ClientSecret = "test-client-secret-never-log-716c";
    private const string RefreshToken = "test-refresh-token-never-log-35af";

    [Fact]
    public void Secret_text_does_not_expose_a_conversion_or_value()
    {
        var conversions = typeof(SecretText)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit");
        Assert.Empty(conversions);
        Assert.Null(typeof(SecretText).GetProperty("Value"));
    }

    [Fact]
    public void Rendering_hides_every_sentinel()
    {
        var password = SecretText.From(Password);
        var secret = SecretText.From(ClientSecret);
        var token = SecretText.From(RefreshToken);
        var interpolated = $"password={password}";
        var printed = new Printable(secret).ToString();
        var json = JsonSerializer.Serialize(new Envelope(token, [password, secret], new Nested(secret)));
        var audit = JsonSerializer.Serialize(new AuditRecord
        {
            OperationId = OperationId.New(),
            TimestampUtc = DateTimeOffset.UnixEpoch,
            Action = "preview",
            Result = "recorded",
            ProtectedValue = password,
            RequestedState = AuditRecord.Redacted(secret),
        });
        var exception = new InvalidOperationException($"sign-in {token}");
        exception.Data["password"] = password;
        var diagnostic = DiagnosticSanitizer.Describe(exception);
        var logger = new CaptureLogger();
        SecretSafeLog.Information(
            logger,
            "checked",
            new KeyValuePair<string, object?>("password", password),
            new KeyValuePair<string, object?>("secret", secret));

        foreach (var output in new[] { password.ToString(), interpolated, printed, json, audit, diagnostic, logger.Text })
        {
            Assert.Contains(SecretText.RedactedMarker, output);
            Assert.DoesNotContain(Password, output);
            Assert.DoesNotContain(ClientSecret, output);
            Assert.DoesNotContain(RefreshToken, output);
        }
    }

    [Fact]
    public void Json_refuses_to_read_a_secret_back()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SecretText>($"\"{Password}\""));
    }

    private sealed record Printable(SecretText Secret);

    private sealed record Envelope(SecretText Direct, IReadOnlyList<SecretText> Items, Nested Child);

    private sealed record Nested(SecretText Secret);

    private sealed class CaptureLogger : ILogger
    {
        public string Text { get; private set; } = string.Empty;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Text = formatter(state, exception);
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
            {
                Text += string.Concat(fields.Select(field => field.Value?.ToString()));
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

public sealed class ConfigurationTests
{
    [Fact]
    public void Option_types_have_no_secret_properties()
    {
        ConfigurationGuard.EnsureNoSecretProperties(
            typeof(ApplicationOptions),
            typeof(ActiveDirectoryOptions),
            typeof(MicrosoftGraphOptions),
            typeof(ExchangeOnlineOptions),
            typeof(EntraConnectOptions),
            typeof(DiagnosticsOptions));
    }
}
