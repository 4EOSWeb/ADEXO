using ADEXO.Contracts.Authentication;
using ADEXO.Contracts.Cloud;
using ADEXO.Graph.Authentication;
using ADEXO.Graph.Orchestration;

namespace ADEXO.Graph.Tests;

/// <summary>
/// The tenant lock applies identically whichever mechanism produced the token. T1 is checked on the
/// authentication result itself, and T2 is checked against the directory, before any selected-user read.
/// <para>
/// These tests run the real authentication service with a substituted acquirer, then feed the resulting
/// session into the real pipeline, so a WAM session and a browser session are proven to be treated the
/// same way rather than merely intended to be.
/// </para>
/// </summary>
public sealed class TenantLockAfterEachMethodTests
{
    private static readonly Guid ApprovedTenant = Guid.Parse("c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af");
    private static readonly Guid OtherTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AdGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    private static PublicClientAuthenticationService BrokerService(System.Guid tokenTenant) =>
        new(
            new MicrosoftIdentityConfigurationBuilder().Build(),
            application: null,
            windowHandleValidator: new StubWindowHandleValidator(),
            interactiveAcquirer: new RecordingAcquirer { Result = AuthenticationResults.For(tokenTenant) },
            silentAcquirer: new RequireInteractionSilentAcquirer(),
            browserAcquirer: new RecordingAcquirer(),
            mode: AuthenticationMode.WamOnly);

    private static PublicClientAuthenticationService BrowserService(System.Guid tokenTenant) =>
        new(
            new MicrosoftIdentityConfigurationBuilder().Build(),
            application: null,
            windowHandleValidator: new StubWindowHandleValidator(),
            interactiveAcquirer: new RecordingAcquirer(),
            silentAcquirer: new RequireInteractionSilentAcquirer(),
            browserAcquirer: new RecordingAcquirer { Result = AuthenticationResults.For(tokenTenant) },
            mode: AuthenticationMode.SystemBrowserOnly);

    private static CloudReadSession Session(IMicrosoftAuthenticationService authentication, FakeGraphProvider fake) =>
        new(authentication, new GraphCloudProvider(new DelegatingReadClient(fake), new FakeClock()), ApprovedTenant);

    private static ADEXO.Core.Correlation.AdCorrelationSubject Subject() => new()
    {
        ObjectGuid = AdGuid,
        DistinguishedName = "CN=User,OU=Lab,DC=example,DC=com",
    };

    [Fact]
    public async Task T1_passes_after_a_broker_sign_in()
    {
        var service = BrokerService(ApprovedTenant);
        await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        var fake = new FakeGraphProvider();
        var (tenant, result) = await Session(service, fake).EstablishTenantAsync(CancellationToken.None);

        Assert.True(tenant!.TokenTenantMatched);
        Assert.Equal(CloudSectionState.TenantVerified, result.State);
    }

    [Fact]
    public async Task T1_passes_after_a_browser_sign_in()
    {
        var service = BrowserService(ApprovedTenant);
        await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        var fake = new FakeGraphProvider();
        var (tenant, result) = await Session(service, fake).EstablishTenantAsync(CancellationToken.None);

        Assert.True(tenant!.TokenTenantMatched);
        Assert.Equal(CloudSectionState.TenantVerified, result.State);
    }

    [Fact]
    public async Task T2_passes_after_a_broker_sign_in()
    {
        var service = BrokerService(ApprovedTenant);
        await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        var fake = new FakeGraphProvider { DirectoryTenantId = ApprovedTenant };
        var (tenant, _) = await Session(service, fake).EstablishTenantAsync(CancellationToken.None);

        Assert.True(tenant!.DirectoryTenantMatched);
        Assert.True(tenant.IsVerified);
        Assert.Equal(1, fake.OrganizationReads);
    }

    [Fact]
    public async Task T2_passes_after_a_browser_sign_in()
    {
        var service = BrowserService(ApprovedTenant);
        await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        var fake = new FakeGraphProvider { DirectoryTenantId = ApprovedTenant };
        var (tenant, _) = await Session(service, fake).EstablishTenantAsync(CancellationToken.None);

        Assert.True(tenant!.DirectoryTenantMatched);
        Assert.True(tenant.IsVerified);
        Assert.Equal(1, fake.OrganizationReads);
    }

    [Fact]
    public async Task A_wrong_tenant_is_rejected_after_a_broker_sign_in_and_no_directory_read_happens()
    {
        var service = BrokerService(OtherTenant);
        var signIn = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        // T1 rejects the token before the session is ever adopted.
        Assert.False(signIn.IsSuccess);
        Assert.Equal(AuthenticationState.TenantMismatch, service.State);
        Assert.Null(service.CurrentAccount);
    }

    [Fact]
    public async Task A_wrong_tenant_is_rejected_after_a_browser_sign_in_and_no_directory_read_happens()
    {
        var service = BrowserService(OtherTenant);
        var signIn = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.False(signIn.IsSuccess);
        Assert.Equal(AuthenticationState.TenantMismatch, service.State);
        Assert.Null(service.CurrentAccount);
    }

    [Fact]
    public async Task No_user_read_happens_after_the_browser_path_until_both_locks_pass()
    {
        var service = BrowserService(ApprovedTenant);
        await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        // The directory disagrees with the token, so T2 fails. Correlation must never be reached.
        var fake = new FakeGraphProvider { DirectoryTenantId = OtherTenant };
        var session = Session(service, fake);
        var (tenant, result) = await session.EstablishTenantAsync(CancellationToken.None);

        Assert.False(tenant!.IsVerified);
        Assert.Equal(CloudSectionState.TenantMismatch, result.State);
        Assert.False(fake.UserWasQueried);
        Assert.Equal(0, fake.SkuReads);
    }

    [Fact]
    public async Task A_verified_browser_session_reaches_correlation_with_the_same_rules_as_the_broker()
    {
        var service = BrowserService(ApprovedTenant);
        await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        var fake = new FakeGraphProvider();
        var session = Session(service, fake);
        var (tenant, _) = await session.EstablishTenantAsync(CancellationToken.None);

        var result = await session.CorrelateAsync(Subject(), tenant!, consistencyGuid: null, CancellationToken.None);

        // The synthetic AD object has no matching cloud object, so Unmatched is the correct outcome. The
        // point is that the query happened, which means both locks passed.
        Assert.Equal(CloudSectionState.Unmatched, result.State);
        Assert.True(fake.UserWasQueried);
        Assert.False(result.ShowsUserData);
    }
}
