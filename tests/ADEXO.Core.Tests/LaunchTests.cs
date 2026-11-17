using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Launch;
using ADEXO.Contracts.Time;
using ADEXO.Core.Launch;

namespace ADEXO.Core.Tests;

public sealed class LaunchParserTests
{
    private static readonly IClock Clock = new FixedClock(new DateTimeOffset(2026, 11, 17, 16, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("LDAP://CN=User,DC=example,DC=com")]
    [InlineData("LDAP://CN=O'Brien\\, John,DC=example,DC=com")]
    [InlineData("LDAP://CN=Smith \\(Contractor\\),DC=example,DC=com")]
    [InlineData("LDAP://CN=Test\\+User,DC=example,DC=com")]
    [InlineData("LDAP://CN=Quoted \\\"Name\\\",DC=example,DC=com")]
    public void Accepts_valid_user_paths(string path)
    {
        var outcome = Parse(path, "user");
        Assert.True(outcome.Succeeded);
        Assert.Equal("user", outcome.Request!.ObjectClass);
        Assert.Null(outcome.Request.Path.Server);
    }

    [Fact]
    public void Accepts_a_maximum_length_path()
    {
        var padding = new string('a', 1800);
        var path = "LDAP://CN=" + padding + ",DC=example,DC=com";
        Assert.True(path.Length <= AducLaunchRequestParser.MaximumArgumentLength);
        Assert.True(Parse(path, "USER").Succeeded);
    }

    [Fact]
    public void Empty_launch_is_not_a_search()
    {
        var outcome = new AducLaunchRequestParser(Clock).Parse([], LaunchSource.Unknown);
        Assert.True(outcome.IsEmptyLaunch);
        Assert.False(outcome.Succeeded);
    }

    public static IEnumerable<object[]> InvalidInputs()
    {
        yield return [new[] { "LDAP://CN=User,DC=example,DC=com" }, "argument-count"];
        yield return [new[] { "LDAP://CN=User,DC=example,DC=com", "user", "extra" }, "argument-count"];
        yield return [new[] { " ", "user" }, "missing-value"];
        yield return [new[] { "LDAP://CN=User,DC=example,DC=com", "group" }, "unsupported-class"];
        yield return [new[] { "file://CN=User", "user" }, "unsupported-scheme"];
        yield return [new[] { "http://example.test/user", "user" }, "unsupported-scheme"];
        yield return [new[] { "https://example.test/user", "user" }, "unsupported-scheme"];
        yield return [new[] { "powershell:calc", "user" }, "unsupported-scheme"];
        yield return [new[] { "cmd:calc", "user" }, "unsupported-scheme"];
        yield return [new[] { "shell:startup", "user" }, "unsupported-scheme"];
        yield return [new[] { "javascript:alert(1)", "user" }, "unsupported-scheme"];
        yield return [new[] { "data:text/plain,hi", "user" }, "unsupported-scheme"];
        yield return [new[] { "GC://CN=User,DC=example,DC=com", "user" }, "unsupported-scheme"];
        yield return [new[] { "LDAP://CN=User,DC=example,DC=com && calc.exe", "user" }, "malformed-ldap"];
        yield return [new[] { "LDAP://CN=User,DC=example,DC=com?calc", "user" }, "malformed-ldap"];
        yield return [new[] { "LDAP://", "user" }, "malformed-ldap"];
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public void Rejects_unsafe_or_incomplete_input(string[] arguments, string code)
    {
        var outcome = new AducLaunchRequestParser(Clock).Parse(arguments, LaunchSource.AducStaticContextMenu);
        Assert.False(outcome.Succeeded);
        Assert.Equal(code, outcome.FailureCode);
    }

    [Fact]
    public void Rejects_control_characters_and_excessive_length()
    {
        Assert.Equal("control-character", Parse("LDAP://CN=User\0,DC=example,DC=com", "user").FailureCode);
        Assert.Equal("control-character", Parse("LDAP://CN=User\r,DC=example,DC=com", "user").FailureCode);
        Assert.Equal("control-character", Parse("LDAP://CN=User\n,DC=example,DC=com", "user").FailureCode);
        Assert.Equal("control-character", Parse("LDAP://CN=User\t,DC=example,DC=com", "user").FailureCode);
        var huge = "LDAP://CN=" + new string('a', 2100) + ",DC=example,DC=com";
        Assert.Equal("length", Parse(huge, "user").FailureCode);
    }

    [Fact]
    public void Parser_type_does_not_start_a_process()
    {
        var names = typeof(AducLaunchRequestParser).GetMethods().Select(method => method.Name);
        Assert.DoesNotContain(names, name => name.Contains("Start", StringComparison.Ordinal));
    }

    private static LaunchParseOutcome Parse(string path, string objectClass) =>
        new AducLaunchRequestParser(Clock).Parse([path, objectClass], LaunchSource.AducStaticContextMenu);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}

public sealed class TrustedEndpointTests
{
    private static TrustedDirectoryEndpoint Discovery() =>
        new("dc01.example.com", "DC=example,DC=com", "CN=Configuration,DC=example,DC=com", "DC=example,DC=com");

    [Fact]
    public void Serverless_path_uses_the_discovered_controller()
    {
        var path = Parse("LDAP://CN=User,DC=example,DC=com");
        var selected = TrustedEndpointSelector.Select(path, Discovery());
        Assert.True(selected.Accepted);
        Assert.Equal("dc01.example.com", selected.Endpoint!.HostName);
    }

    [Fact]
    public void Trusted_host_is_accepted()
    {
        var path = Parse("LDAP://dc01.example.com/CN=User,DC=example,DC=com");
        Assert.True(TrustedEndpointSelector.Select(path, Discovery()).Accepted);
    }

    [Theory]
    [InlineData("LDAP://attacker.example/CN=User,DC=example,DC=com", "untrusted-host")]
    [InlineData("LDAP://10.1.2.3/CN=User,DC=example,DC=com", "ip-literal")]
    [InlineData("LDAP://dc01:389/CN=User,DC=example,DC=com", "malformed-ldap")]
    [InlineData("LDAP://dc01/CN=User,DC=example,DC=com", "unqualified-host")]
    [InlineData("LDAP://dc01.example.com/CN=User,DC=other,DC=com", "naming-context")]
    public void Untrusted_hosts_are_rejected(string path, string code)
    {
        var parsed = new AducLaunchRequestParser(new LaunchParserTestsClock()).Parse([path, "user"], LaunchSource.DirectCommandLine);
        if (!parsed.Succeeded)
        {
            Assert.Equal(code, parsed.FailureCode);
            return;
        }

        var selected = TrustedEndpointSelector.Select(parsed.Request!.Path, Discovery());
        Assert.False(selected.Accepted);
        Assert.Equal(code, selected.FailureCode);
    }

    private static ParsedLdapPath Parse(string path)
    {
        var outcome = new AducLaunchRequestParser(new LaunchParserTestsClock()).Parse([path, "user"], LaunchSource.DirectCommandLine);
        Assert.True(outcome.Succeeded);
        return outcome.Request!.Path;
    }

    private sealed class LaunchParserTestsClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UnixEpoch;
    }
}
