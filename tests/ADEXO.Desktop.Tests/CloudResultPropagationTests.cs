using ADEXO.Contracts.Authentication;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Launch;
using ADEXO.Contracts.Providers;
using ADEXO.Contracts.Results;
using ADEXO.Contracts.Time;
using ADEXO.Core.Launch;
using ADEXO.Graph.Orchestration;

namespace ADEXO.Desktop.Tests;

/// <summary>
/// Regression coverage for two defects the first live browser sign-in exposed.
/// <para>
/// The browser path authenticated and both tenant locks passed, but the Microsoft 365 tab still read
/// "Disconnected" because nothing rebuilt the sections when the cloud result changed. Separately, the
/// Diagnostics tab was never populated at all, so the one artefact an administrator copies into a
/// support case was always empty.
/// </para>
/// </summary>
public sealed class CloudResultPropagationTests
{
    private static readonly Guid ApprovedTenant = Guid.Parse("c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af");

    private static VerifiedDirectoryUser AdUser() => new()
    {
        ObjectGuid = Guid.Parse("a116a060-4fc6-482b-be01-494c2cf4606e"),
        DistinguishedName = "CN=User,OU=Lab,DC=example,DC=com",
        CanonicalName = "example.com/Lab/User",
        ObjectClass = "user",
        DisplayName = "Example User",
        SamAccountName = "user",
        Stage = IdentityStage.DisplayDataLoaded,
    };

    private static (MainViewModel Model, CloudSectionViewModel Cloud, ProductViewModel Product) Build(
        IMicrosoftAuthenticationService? authentication = null)
    {
        var model = new MainViewModel(
            new AducLaunchRequestParser(new FixedClock()),
            new FakeDirectory { User = AdUser() },
            new FixedClock());

        authentication ??= new StubAuthenticationService
        {
            CurrentAccount = AuthenticatedAccount.Create(null, ApprovedTenant, "administrator@testdomain387.onmicrosoft.com", brokerUsed: false),
        };

        var provider = new ADEXO.Graph.GraphCloudProvider(new FixedTenantReadClient(ApprovedTenant), new FixedClock());
        var session = new CloudReadSession(authentication, provider, ApprovedTenant);
        var cloud = new CloudSectionViewModel(authentication, session, () => null, () => IntPtr.Zero);
        var product = new ProductViewModel(cloud, mailboxes: null, () => cloud.VerifiedEntraObjectId);

        model.AttachCloud(cloud);
        model.AttachProduct(product);

        return (model, cloud, product);
    }

    private static string StatusOf(ProductViewModel product) =>
        product.Microsoft365.Groups
            .Single(group => group.Title == "Authentication")
            .Rows.Single(row => row.Label == "Status")
            .Value;

    [Fact]
    public async Task A_completed_sign_in_rebuilds_the_microsoft_365_section()
    {
        var (_, cloud, product) = Build();

        // Before signing in the section reports a signed-out session rather than a connected one.
        Assert.Equal("Disconnected", StatusOf(product));

        await cloud.SignInAsync(IntPtr.Zero);

        // The live defect: both tenant locks passed and the tab still said "Disconnected".
        Assert.Equal(CloudSectionState.TenantVerified, cloud.State);
        Assert.Equal("Connected", StatusOf(product));
    }

    [Fact]
    public async Task A_completed_sign_in_shows_the_signed_in_administrator_and_tenant()
    {
        var (_, cloud, product) = Build();

        await cloud.SignInAsync(IntPtr.Zero);

        var rows = product.Microsoft365.Groups.Single(group => group.Title == "Authentication").Rows;
        Assert.Contains(rows, row => row.Label == "Signed in as"
            && row.Value.Contains("administrator@testdomain387", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Label == "Tenant"
            && row.Value.StartsWith("c18ee4be", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Signing_out_returns_the_section_to_disconnected()
    {
        var (_, cloud, product) = Build();

        await cloud.SignInAsync(IntPtr.Zero);
        Assert.Equal("Connected", StatusOf(product));

        await cloud.SignOutAsync();

        Assert.Equal("Disconnected", StatusOf(product));
    }

    [Fact]
    public async Task A_cloud_change_keeps_the_selected_ad_user_loaded()
    {
        // A cloud result carries no AD object. If it blanked the AD identity, the Overview tab would read
        // "no user selected" immediately after a successful sign-in.
        var (model, cloud, product) = Build();
        await model.StartAsync(["LDAP://CN=User,OU=Lab,DC=example,DC=com", "user"]);

        await cloud.SignInAsync(IntPtr.Zero);

        Assert.Equal(ReadOnlyViewState.Loaded, model.State);
        Assert.Contains(
            product.Overview.Groups.Single(group => group.Title == "Identity").Rows,
            row => row.Label == "Display name" && row.Value == "Example User");
    }

    [Fact]
    public void Diagnostics_are_populated_rather_than_left_empty()
    {
        // The Diagnostics tab is what an administrator copies into a support case. It was never
        // populated, so it always showed the empty-state text.
        var (_, _, product) = Build();

        product.RefreshDiagnostics(TimeSpan.FromMilliseconds(250));

        Assert.True(product.Diagnostics.HasContent);
        Assert.False(product.Diagnostics.ShowEmptyState);
    }

    [Fact]
    public void Diagnostics_name_the_preferred_and_actual_authentication_method()
    {
        var (_, _, product) = Build();

        product.RefreshDiagnostics();

        var auth = product.Diagnostics.Groups.Single(group => group.Title == "Authentication method");
        Assert.Contains(auth.Rows, row => row.Label == "Preferred");
        Assert.Contains(auth.Rows, row => row.Label == "Actual");
        Assert.Contains(auth.Rows, row => row.Label == "Fallback reason");
        Assert.Contains(auth.Rows, row => row.Label == "Broker start elapsed");
    }

    [Fact]
    public void Diagnostics_contain_no_token_shaped_value()
    {
        var (_, _, product) = Build();

        product.RefreshDiagnostics();

        var values = product.Diagnostics.Groups.SelectMany(group => group.Rows).Select(row => row.Value);
        Assert.DoesNotContain(values, value => value.Contains("eyJ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancelling_an_in_flight_sign_in_leaves_every_command_usable_again()
    {
        // The live defect: an operator closed the browser window, the sign-in never returned, and every
        // command stayed disabled, so the only way out was to kill the process.
        var (model, _, product) = Build(authentication: new HangingAuthenticationService());
        await model.StartAsync(["LDAP://CN=User,OU=Lab,DC=example,DC=com", "user"]);

        var signIn = model.Cloud.SignInAsync(IntPtr.Zero);

        Assert.False(model.Cloud.SignInCommand.CanExecute(null));
        Assert.True(model.Cloud.CancelCommand.CanExecute(null));

        model.Cloud.CancelAttempt();

        // The attempt must not remain pending indefinitely.
        var completed = await Task.WhenAny(signIn, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(signIn, completed);

        Assert.Equal(CloudSectionState.Canceled, model.Cloud.State);
        Assert.Contains("unaffected", model.Cloud.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(model.Cloud.SignInCommand.CanExecute(null));
        Assert.False(model.Cloud.CancelCommand.CanExecute(null));
        Assert.Equal(ReadOnlyViewState.Loaded, model.State);
        Assert.Equal("Example User", product.StatusLine);
    }

    [Fact]
    public async Task Cancelling_a_sign_in_clears_any_cloud_state_from_the_tabs()
    {
        var (model, _, product) = Build(authentication: new HangingAuthenticationService());
        await model.StartAsync(["LDAP://CN=User,OU=Lab,DC=example,DC=com", "user"]);

        var signIn = model.Cloud.SignInAsync(IntPtr.Zero);
        model.Cloud.CancelAttempt();
        await signIn;

        Assert.Equal("Disconnected", StatusOf(product));
    }

    /// <summary>
    /// A sign-in that never returns, modelling a browser window the operator closed. It honours the
    /// cancellation token, which is the contract the section relies on to stay usable.
    /// </summary>
    private sealed class HangingAuthenticationService : IMicrosoftAuthenticationService
    {
        public AuthenticationState State { get; set; } = AuthenticationState.Authenticating;

        public AuthenticatedAccount? CurrentAccount { get; set; }

        public Task<ProviderResult<AuthenticatedAccount>> TryAcquireSilentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<AuthenticatedAccount>.Failed(
                OperationError.Create(FailureCategory.Authentication, "No account.")));

        public async Task<ProviderResult<AuthenticatedAccount>> SignInAsync(IntPtr ownerWindowHandle, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("unreachable");
        }

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            CurrentAccount = null;
            State = AuthenticationState.SignedOut;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 18, 19, 0, 0, TimeSpan.Zero);
    }

    /// <summary>A read client that reports an approved tenant and performs no network call.</summary>
    private sealed class FixedTenantReadClient(Guid tenantId) : ADEXO.Graph.Reads.IGraphReadClient
    {
        public Task<ProviderResult<Guid>> ReadOrganizationIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<Guid>.Success(tenantId));

        public Task<ProviderResult<IReadOnlyList<CloudDirectoryUser>>> ReadUsersByImmutableIdAsync(
            string escapedImmutableId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<IReadOnlyList<CloudDirectoryUser>>.Success((IReadOnlyList<CloudDirectoryUser>)[]));

        public Task<ProviderResult<IReadOnlyList<CloudSubscribedSku>>> ReadSubscribedSkusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<IReadOnlyList<CloudSubscribedSku>>.Success((IReadOnlyList<CloudSubscribedSku>)[]));
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
            Task.FromResult(ProviderResult<VerifiedDirectoryUser>.Success(User!));

        public Task<ProviderResult<VerifiedDirectoryUser>> RefreshByObjectGuidAsync(
            Guid objectGuid,
            CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<VerifiedDirectoryUser>.Success(User!));
    }
}
