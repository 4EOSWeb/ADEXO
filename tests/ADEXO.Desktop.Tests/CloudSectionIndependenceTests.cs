using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Launch;
using ADEXO.Contracts.Providers;
using ADEXO.Contracts.Results;
using ADEXO.Contracts.Time;
using ADEXO.Core.Launch;
using ADEXO.Graph.Orchestration;

namespace ADEXO.Desktop.Tests;

/// <summary>
/// Proves the Microsoft 365 section cannot damage the Active Directory view. A cloud failure must leave
/// the AD user loaded and its identity unchanged.
/// </summary>
public sealed class CloudSectionIndependenceTests
{
    private static readonly string[] UserLaunch = ["LDAP://CN=User,OU=Lab,DC=example,DC=com", "user"];

    [Fact]
    public async Task An_unconfigured_cloud_section_does_not_disturb_a_loaded_ad_user()
    {
        var model = await LoadedModelAsync();

        Assert.Equal(ReadOnlyViewState.Loaded, model.State);
        Assert.Equal(CloudSectionState.NotConfigured, model.Cloud.State);
        Assert.False(model.Cloud.IsConfigured);
        Assert.False(model.Cloud.ShowUserData);
        Assert.Equal(Sample().ObjectGuid, model.User!.ObjectGuid);
    }

    [Fact]
    public async Task A_loaded_ad_user_survives_a_cloud_section_state_change()
    {
        var model = await LoadedModelAsync();
        var guidBefore = model.User!.ObjectGuid;

        // The unconfigured section refuses to sign in and reports it, without touching AD state.
        await model.Cloud.SignInAsync(IntPtr.Zero);

        Assert.Equal(ReadOnlyViewState.Loaded, model.State);
        Assert.Equal(guidBefore, model.User!.ObjectGuid);
        Assert.Equal(CloudSectionState.NotConfigured, model.Cloud.State);
    }

    [Fact]
    public async Task Signing_out_of_an_unconfigured_section_leaves_ad_intact()
    {
        var model = await LoadedModelAsync();
        var guidBefore = model.User!.ObjectGuid;

        await model.Cloud.SignOutAsync();

        Assert.Equal(ReadOnlyViewState.Loaded, model.State);
        Assert.Equal(guidBefore, model.User!.ObjectGuid);
        Assert.False(model.Cloud.ShowUserData);
    }

    [Fact]
    public async Task Refreshing_an_unconfigured_section_leaves_ad_intact()
    {
        var model = await LoadedModelAsync();
        var guidBefore = model.User!.ObjectGuid;

        await model.Cloud.RefreshAsync(null);

        Assert.Equal(ReadOnlyViewState.Loaded, model.State);
        Assert.Equal(guidBefore, model.User!.ObjectGuid);
        Assert.Equal(CloudSectionState.NotConfigured, model.Cloud.State);
    }

    [Fact]
    public void The_cloud_section_exposes_no_write_command()
    {
        var model = new MainViewModel(
            new AducLaunchRequestParser(new FixedClock()),
            new FakeDirectory { User = Sample() },
            new FixedClock());

        var names = model.Cloud.GetType()
            .GetProperties()
            .Where(p => typeof(System.Windows.Input.ICommand).IsAssignableFrom(p.PropertyType))
            .Select(p => p.Name)
            .ToArray();

        Assert.Equal(["RefreshCommand", "SignInCommand", "SignOutCommand"], names.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Signing_out_clears_the_signed_in_identity_and_tenant_display()
    {
        // The live lab found the signed-in account and tenant still displayed after sign-out, which
        // leaves a stale identity visible in a signed-out state.
        var stub = new StubAuthenticationService
        {
            CurrentAccount = Contracts.Cloud.AuthenticatedAccount.Create(
                null,
                Guid.Parse("c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af"),
                "admin@lab.example",
                true),
        };
        var provider = new ADEXO.Graph.GraphCloudProvider(new NeverCalledReadClient(), new FixedClock());
        var session = new ADEXO.Graph.Orchestration.CloudReadSession(stub, provider, Guid.Parse("c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af"));
        var section = new CloudSectionViewModel(stub, session, () => null, () => IntPtr.Zero);

        await section.SignOutAsync();

        Assert.Equal(CloudSectionState.SignedOut, section.State);
        Assert.Null(section.SignedInAs);
        Assert.Null(section.TenantDisplay);
        Assert.False(section.ShowUserData);
    }

    [Fact]
    public async Task The_cloud_section_never_reports_user_data_without_verification()
    {
        var model = await LoadedModelAsync();

        Assert.False(model.Cloud.ShowUserData);
        Assert.Null(model.Cloud.EntraObjectId);
        Assert.Null(model.Cloud.CloudUserPrincipalName);
        Assert.Equal(string.Empty, model.Cloud.LicenseSummary);
    }

    [Fact]
    public void A_configured_section_starts_signed_out_rather_than_not_configured()
    {
        // The live lab found the configured section reporting NotConfigured, because the state field
        // defaulted and nothing set it at construction. That reads as a configuration failure.
        var stub = new StubAuthenticationService();
        var provider = new ADEXO.Graph.GraphCloudProvider(
            new NeverCalledReadClient(),
            new FixedClock());
        var session = new ADEXO.Graph.Orchestration.CloudReadSession(stub, provider, Guid.Parse("c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af"));

        var section = new CloudSectionViewModel(stub, session, () => null, () => IntPtr.Zero);

        Assert.True(section.IsConfigured);
        Assert.Equal(CloudSectionState.SignedOut, section.State);
        Assert.DoesNotContain("not configured", section.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_section_built_without_configuration_reports_the_reason_it_received()
    {
        var section = CloudSectionViewModel.NotConfigured("The configured tenant identifier is not valid.");

        Assert.False(section.IsConfigured);
        Assert.Equal(CloudSectionState.NotConfigured, section.State);
        Assert.Equal("The configured tenant identifier is not valid.", section.Message);
    }

    private static async Task<MainViewModel> LoadedModelAsync()
    {
        var model = new MainViewModel(
            new AducLaunchRequestParser(new FixedClock()),
            new FakeDirectory { User = Sample() },
            new FixedClock());

        await model.StartAsync(UserLaunch);
        return model;
    }

    private static VerifiedDirectoryUser Sample() => new()
    {
        ObjectGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
        DistinguishedName = "CN=User,OU=Lab,DC=example,DC=com",
        CanonicalName = "example.com/Lab/User",
        ObjectClass = "user",
        DisplayName = "Example User",
        Stage = IdentityStage.DisplayDataLoaded,
    };

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 18, 19, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeDirectory : IActiveDirectoryProvider
    {
        public VerifiedDirectoryUser? User { get; init; }

        public Task<Contracts.Capabilities.ProviderCapability> GetCapabilityAsync(
            Contracts.Features.KnownFeatureId feature,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ProviderResult<TrustedDirectoryEndpoint>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<TrustedDirectoryEndpoint>.Success(
                new TrustedDirectoryEndpoint("dc01.example.com", "DC=example,DC=com", "CN=Configuration,DC=example,DC=com", null)));

        public Task<ProviderResult<VerifiedDirectoryUser>> ResolveUserAsync(
            AducLaunchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(User is null
                ? ProviderResult<VerifiedDirectoryUser>.Failed(OperationError.Create(FailureCategory.NotFound, "missing"))
                : ProviderResult<VerifiedDirectoryUser>.Success(User));

        public Task<ProviderResult<VerifiedDirectoryUser>> RefreshByObjectGuidAsync(
            Guid objectGuid,
            CancellationToken cancellationToken) =>
            Task.FromResult(User is null
                ? ProviderResult<VerifiedDirectoryUser>.Failed(OperationError.Create(FailureCategory.NotFound, "missing"))
                : ProviderResult<VerifiedDirectoryUser>.Success(User));
    }
}
