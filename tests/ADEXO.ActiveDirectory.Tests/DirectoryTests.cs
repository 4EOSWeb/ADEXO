using System.Reflection;
using ADEXO.ActiveDirectory;
using ADEXO.ActiveDirectory.Ldap;
using ADEXO.Contracts.Authority;
using ADEXO.Contracts.Capabilities;
using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Features;
using ADEXO.Contracts.Launch;
using ADEXO.Contracts.Providers;
using ADEXO.Contracts.Results;
using ADEXO.Contracts.Time;
using ADEXO.Core.Launch;

namespace ADEXO.ActiveDirectory.Tests;

public sealed class LdapFilterTests
{
    [Theory]
    [InlineData("\\", "\\5C")]
    [InlineData("*", "\\2A")]
    [InlineData("(", "\\28")]
    [InlineData(")", "\\29")]
    [InlineData("a\0b", "a\\00b")]
    public void Escapes_rfc4515_characters(string value, string expected) =>
        Assert.Equal(expected, LdapFilter.Escape(value));

    [Fact]
    public void Rejects_filter_injection_by_encoding()
    {
        var encoded = LdapFilter.Escape("user)(|(objectClass=*)");
        Assert.DoesNotContain(")(|", encoded);
        Assert.Contains("\\29", encoded);
    }

    [Fact]
    public void Object_guid_filter_uses_the_active_directory_byte_order()
    {
        var guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        const string expected = "(objectGUID=\\33\\22\\11\\00\\55\\44\\77\\66\\88\\99\\AA\\BB\\CC\\DD\\EE\\FF)";
        Assert.Equal(expected, LdapFilter.ObjectGuidFilter(guid));
        Assert.Equal(guid.ToByteArray(), ConvertFilterToBytes(LdapFilter.ObjectGuidFilter(guid)));
        Assert.DoesNotContain(guid.ToString(), LdapFilter.ObjectGuidFilter(guid), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unicode_values_are_preserved()
    {
        Assert.Equal("Åsa", LdapFilter.Escape("Åsa"));
    }

    private static byte[] ConvertFilterToBytes(string filter)
    {
        var hex = filter.Replace("(objectGUID=", string.Empty, StringComparison.Ordinal).TrimEnd(')').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return hex.Select(value => Convert.ToByte(value, 16)).ToArray();
    }
}

public sealed class AttributeConversionTests
{
    [Fact]
    public void Converts_sid_guid_and_account_flags()
    {
        var guid = Guid.NewGuid();
        Assert.Equal(guid, AttributeConverter.ReadGuid(guid.ToByteArray()));
        Assert.Null(AttributeConverter.ReadGuid([1, 2, 3, 4]));
        var sid = new byte[] { 1, 2, 0, 0, 0, 0, 0, 5, 32, 0, 0, 0, 32, 2, 0, 0 };
        Assert.Equal("S-1-5-32-544", AttributeConverter.ReadSid(sid));
        Assert.Equal(AccountEnabledState.Disabled, AttributeConverter.ReadEnabled("2"));
        Assert.Equal(AccountEnabledState.Enabled, AttributeConverter.ReadEnabled("512"));
        Assert.Equal(AccountExpirationKind.Never, AttributeConverter.ReadAccountExpiration("0").Kind);
        Assert.Equal(AccountExpirationKind.Never, AttributeConverter.ReadAccountExpiration(long.MaxValue.ToString()).Kind);
        Assert.Equal(AccountExpirationKind.Invalid, AttributeConverter.ReadAccountExpiration("nope").Kind);
        Assert.Equal(PasswordLastSetKind.MustChangeAtNextLogon, AttributeConverter.ReadPasswordLastSet("0").Kind);
        Assert.Equal(PasswordLastSetKind.Unavailable, AttributeConverter.ReadPasswordLastSet(null).Kind);
        Assert.Equal(LockoutIndication.TimeSetOnThisReplica, AttributeConverter.ReadLockout("1"));
        Assert.Equal(LockoutIndication.NotIndicated, AttributeConverter.ReadLockout("0"));
        Assert.NotNull(AttributeConverter.ReadGeneralizedTime("20261117120000.0Z"));
    }
}

public sealed class IdentityResolutionTests
{
    private static readonly Guid ObjectId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    [Fact]
    public async Task Matching_guid_rebind_is_required()
    {
        var reader = new ScriptedReader(Bag(ObjectId, "CN=User,DC=example,DC=com"), Bag(ObjectId, "CN=User,DC=example,DC=com"));
        var provider = new ActiveDirectoryProvider(reader, new FixedTime());
        var result = await provider.ResolveUserAsync(Request(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(ObjectId, result.Value!.ObjectGuid);
        Assert.Equal(ObjectId, reader.RequestedGuid);
        Assert.False((await provider.GetCapabilityAsync(KnownFeatureId.ActiveDirectoryUserIdentityRead, CancellationToken.None)).CanWrite);
    }

    [Fact]
    public async Task Mismatched_or_missing_guid_fails_closed()
    {
        var mismatch = new ActiveDirectoryProvider(new ScriptedReader(Bag(ObjectId, "CN=User,DC=example,DC=com"), Bag(Guid.NewGuid(), "CN=User,DC=example,DC=com")), new FixedTime());
        Assert.Equal(FailureCategory.VerificationFailed, (await mismatch.ResolveUserAsync(Request(), CancellationToken.None)).Error!.Category);
        var missing = new ActiveDirectoryProvider(new ScriptedReader(Bag(null, "CN=User,DC=example,DC=com"), null), new FixedTime());
        Assert.False((await missing.ResolveUserAsync(Request(), CancellationToken.None)).IsSuccess);
        var malformed = new ActiveDirectoryProvider(new ScriptedReader(Bag(ObjectId, "CN=User,DC=example,DC=com", malformedGuid: true), null), new FixedTime());
        Assert.False((await malformed.ResolveUserAsync(Request(), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task Moved_object_keeps_the_guid_and_a_deleted_object_fails()
    {
        var moved = new ActiveDirectoryProvider(new ScriptedReader(Bag(ObjectId, "CN=Old,DC=example,DC=com"), Bag(ObjectId, "CN=New,DC=example,DC=com")), new FixedTime());
        var movedResult = await moved.ResolveUserAsync(Request(), CancellationToken.None);
        Assert.Equal("CN=New,DC=example,DC=com", movedResult.Value!.DistinguishedName);
        var deleted = new ActiveDirectoryProvider(new ScriptedReader(Bag(ObjectId, "CN=User,DC=example,DC=com"), null), new FixedTime());
        Assert.Equal(FailureCategory.NotFound, (await deleted.ResolveUserAsync(Request(), CancellationToken.None)).Error!.Category);
    }

    [Fact]
    public async Task Unavailable_domain_and_denied_read_stay_read_only()
    {
        var down = new ActiveDirectoryProvider(new ScriptedReader(null, null, FailureCategory.Network), new FixedTime());
        var unavailable = await down.GetCapabilityAsync(KnownFeatureId.ActiveDirectoryUserContactRead, CancellationToken.None);
        Assert.False(unavailable.Available);
        Assert.False(unavailable.CanWrite);
        var denied = new ActiveDirectoryProvider(new ScriptedReader(null, null, FailureCategory.Authorization), new FixedTime());
        var unauthorized = await denied.GetCapabilityAsync(KnownFeatureId.ActiveDirectoryUserMetadataRead, CancellationToken.None);
        Assert.False(unauthorized.Authorized);
        Assert.False(unauthorized.CanWrite);
        var unsupported = await denied.GetCapabilityAsync(KnownFeatureId.ActiveDirectoryUserProxyAddresses, CancellationToken.None);
        Assert.False(unsupported.CanWrite);
    }

    [Fact]
    public void Public_provider_surface_has_no_write_method()
    {
        var names = typeof(IActiveDirectoryProvider).GetMethods().Select(method => method.Name)
            .Concat(typeof(IDirectoryReader).GetMethods().Select(method => method.Name));
        Assert.DoesNotContain(names, name => name.Contains("Write", StringComparison.Ordinal)
            || name.Contains("Modify", StringComparison.Ordinal)
            || name.Contains("Delete", StringComparison.Ordinal)
            || name.Contains("Add", StringComparison.Ordinal));
        var source = File.ReadAllText(Path.Combine(FindSource(), "LdapDirectoryReader.cs"));
        Assert.DoesNotContain("ModifyRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteRequest", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Connection_security_refuses_weaker_authentication()
    {
        var security = new LdapConnectionSecurity(TimeSpan.FromSeconds(15));
        Assert.Equal(System.DirectoryServices.Protocols.AuthType.Negotiate, security.Authentication);
        Assert.True(security.Signing);
        Assert.True(security.Sealing);
        Assert.Equal(System.DirectoryServices.Protocols.ReferralChasingOptions.None, security.Referrals);
        Assert.Contains("not canceled", LdapConnectionSecurity.CancellationBoundary, StringComparison.OrdinalIgnoreCase);
    }

    private static AducLaunchRequest Request()
    {
        var parsed = new AducLaunchRequestParser(new Clock()).Parse(
            ["LDAP://CN=User,DC=example,DC=com", "user"],
            LaunchSource.AducStaticContextMenu);
        return parsed.Request!;
    }

    private static DirectoryAttributeBag Bag(Guid? guid, string distinguishedName, bool malformedGuid = false)
    {
        var text = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["distinguishedName"] = [distinguishedName],
            ["objectClass"] = ["top", "person", "organizationalPerson", "user"],
            ["displayName"] = ["Example User"],
            ["sAMAccountName"] = ["example"],
            ["userAccountControl"] = ["512"],
        };
        var binary = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (malformedGuid)
        {
            binary["objectGUID"] = [1, 2, 3, 4];
        }
        else if (guid is not null)
        {
            binary["objectGUID"] = guid.Value.ToByteArray();
        }

        return new DirectoryAttributeBag(text, binary);
    }

    private static string FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "ADEXO.ActiveDirectory", "LdapDirectoryReader.cs");
            if (File.Exists(candidate))
            {
                return Path.Combine(directory.FullName, "src", "ADEXO.ActiveDirectory");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The Active Directory source was not found.");
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 17, 16, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 17, 16, 0, 0, TimeSpan.Zero);
    }

    private sealed class ScriptedReader(DirectoryAttributeBag? first, DirectoryAttributeBag? byGuid, FailureCategory? discoveryFailure = null) : IDirectoryReader
    {
        public Guid? RequestedGuid { get; private set; }

        public Task<ProviderResult<TrustedDirectoryEndpoint>> ReadRootDseAsync(CancellationToken cancellationToken)
        {
            if (discoveryFailure is not null)
            {
                return Task.FromResult(ProviderResult<TrustedDirectoryEndpoint>.Failed(OperationError.Create(discoveryFailure.Value, "discovery failed")));
            }

            return Task.FromResult(ProviderResult<TrustedDirectoryEndpoint>.Success(
                new TrustedDirectoryEndpoint("dc01.example.com", "DC=example,DC=com", "CN=Configuration,DC=example,DC=com", "DC=example,DC=com")));
        }

        public Task<ProviderResult<DirectoryAttributeBag>> ReadByDistinguishedNameAsync(TrustedDirectoryEndpoint endpoint, DistinguishedName distinguishedName, CancellationToken cancellationToken) =>
            Task.FromResult(first is null
                ? ProviderResult<DirectoryAttributeBag>.Failed(OperationError.Create(FailureCategory.NotFound, "missing"))
                : ProviderResult<DirectoryAttributeBag>.Success(first));

        public Task<ProviderResult<DirectoryAttributeBag>> ReadByObjectGuidAsync(TrustedDirectoryEndpoint endpoint, Guid objectGuid, CancellationToken cancellationToken)
        {
            RequestedGuid = objectGuid;
            return Task.FromResult(byGuid is null
                ? ProviderResult<DirectoryAttributeBag>.Failed(OperationError.Create(FailureCategory.NotFound, "deleted"))
                : ProviderResult<DirectoryAttributeBag>.Success(byGuid));
        }
    }
}
