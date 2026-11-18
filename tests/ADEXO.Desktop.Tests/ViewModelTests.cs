using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Launch;
using ADEXO.Contracts.Providers;
using ADEXO.Contracts.Results;
using ADEXO.Contracts.Time;
using ADEXO.Core.Launch;

namespace ADEXO.Desktop.Tests;

public sealed class ViewModelTests
{
    [Fact]
    public async Task No_arguments_do_not_search()
    {
        var directory = new FakeDirectory();
        var model = new MainViewModel(new AducLaunchRequestParser(new Clock()), directory, new Clock());
        await model.StartAsync([]);
        Assert.Equal(ReadOnlyViewState.NoLaunchRequest, model.State);
        Assert.Equal(0, directory.ResolveCalls);
        Assert.Null(model.User);
    }

    [Fact]
    public async Task Verified_user_is_shown_and_refresh_uses_the_guid()
    {
        var directory = new FakeDirectory { User = Sample() };
        var model = new MainViewModel(new AducLaunchRequestParser(new Clock()), directory, new Clock());
        await model.StartAsync(["LDAP://CN=User,DC=example,DC=com", "user"]);
        Assert.Equal(ReadOnlyViewState.Loaded, model.State);
        Assert.True(model.ShowUser);
        await model.RefreshAsync();
        Assert.Equal(Sample().ObjectGuid, directory.RefreshedGuid);
    }

    [Theory]
    [InlineData(FailureCategory.NotFound, ReadOnlyViewState.NotFound)]
    [InlineData(FailureCategory.Authorization, ReadOnlyViewState.AccessDenied)]
    [InlineData(FailureCategory.Timeout, ReadOnlyViewState.DomainUnavailable)]
    [InlineData(FailureCategory.VerificationFailed, ReadOnlyViewState.IdentityVerificationFailed)]
    [InlineData(FailureCategory.Unsupported, ReadOnlyViewState.UnsupportedClass)]
    public async Task Provider_failures_map_to_read_only_states(FailureCategory category, ReadOnlyViewState state)
    {
        var directory = new FakeDirectory { Error = OperationError.Create(category, "failed") };
        var model = new MainViewModel(new AducLaunchRequestParser(new Clock()), directory, new Clock());
        await model.StartAsync(["LDAP://CN=User,DC=example,DC=com", "user"]);
        Assert.Equal(state, model.State);
        Assert.False(model.ShowUser);
    }

    [Fact]
    public async Task Invalid_argument_and_stale_refresh_do_not_expose_the_path()
    {
        var model = new MainViewModel(new AducLaunchRequestParser(new Clock()), new FakeDirectory(), new Clock());
        await model.StartAsync(["http://example.test", "user"]);
        Assert.Equal(ReadOnlyViewState.ValidationFailure, model.State);
        var copied = model.BuildDiagnostics(includeSensitive: false);
        Assert.DoesNotContain("http://", copied, StringComparison.OrdinalIgnoreCase);
        model.Cancel();
        Assert.Equal(ReadOnlyViewState.Canceled, model.State);
        Assert.Contains("not reported as canceled", model.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Default_diagnostics_omit_the_distinguished_name()
    {
        var directory = new FakeDirectory { User = Sample() };
        var model = new MainViewModel(new AducLaunchRequestParser(new Clock()), directory, new Clock());
        await model.StartAsync(["LDAP://CN=User,DC=example,DC=com", "user"]);
        Assert.DoesNotContain("CN=User", model.BuildDiagnostics(false), StringComparison.Ordinal);
        Assert.Contains("Sensitive directory identifiers", model.BuildDiagnostics(true), StringComparison.Ordinal);
    }

    private static VerifiedDirectoryUser Sample() => new()
    {
        ObjectGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
        DistinguishedName = "CN=User,DC=example,DC=com",
        ObjectClass = "user",
        DisplayName = "Example User",
        Stage = IdentityStage.DisplayDataLoaded,
    };

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 17, 16, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeDirectory : IActiveDirectoryProvider
    {
        public int ResolveCalls { get; private set; }

        public Guid? RefreshedGuid { get; private set; }

        public VerifiedDirectoryUser? User { get; init; }

        public OperationError? Error { get; init; }

        public Task<Contracts.Capabilities.ProviderCapability> GetCapabilityAsync(Contracts.Features.KnownFeatureId feature, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderResult<TrustedDirectoryEndpoint>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<TrustedDirectoryEndpoint>.Success(new TrustedDirectoryEndpoint("dc01.example.com", "DC=example,DC=com", "CN=Configuration,DC=example,DC=com", null)));

        public Task<ProviderResult<VerifiedDirectoryUser>> ResolveUserAsync(AducLaunchRequest request, CancellationToken cancellationToken)
        {
            ResolveCalls++;
            return Task.FromResult(Result());
        }

        public Task<ProviderResult<VerifiedDirectoryUser>> RefreshByObjectGuidAsync(Guid objectGuid, CancellationToken cancellationToken)
        {
            RefreshedGuid = objectGuid;
            return Task.FromResult(Result());
        }

        private ProviderResult<VerifiedDirectoryUser> Result() =>
            Error is null && User is not null
                ? ProviderResult<VerifiedDirectoryUser>.Success(User)
                : ProviderResult<VerifiedDirectoryUser>.Failed(Error ?? OperationError.Create(FailureCategory.Unknown, "failed"));
    }
}
