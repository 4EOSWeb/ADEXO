using ADEXO.Contracts.Authentication;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Results;
using ADEXO.Core.Correlation;
using ADEXO.Graph.Orchestration;

namespace ADEXO.Graph.Tests;

/// <summary>A controllable authentication stand-in. It lives in the test assembly only.</summary>
internal sealed class FakeAuthenticationService : IMicrosoftAuthenticationService
{
    public AuthenticationState State { get; set; } = AuthenticationState.SignedOut;

    public AuthenticatedAccount? CurrentAccount { get; set; }

    public int SignInCalls { get; private set; }

    public ProviderResult<AuthenticatedAccount>? SignInResult { get; set; }

    public Task<ProviderResult<AuthenticatedAccount>> TryAcquireSilentAsync(CancellationToken cancellationToken) =>
        Task.FromResult(CurrentAccount is null
            ? ProviderResult<AuthenticatedAccount>.Failed(OperationError.Create(FailureCategory.Authentication, "No account."))
            : ProviderResult<AuthenticatedAccount>.Success(CurrentAccount));

    public Task<ProviderResult<AuthenticatedAccount>> SignInAsync(IntPtr ownerWindowHandle, CancellationToken cancellationToken)
    {
        SignInCalls++;
        return Task.FromResult(SignInResult ?? ProviderResult<AuthenticatedAccount>.Success(CurrentAccount!));
    }

    public Task SignOutAsync(CancellationToken cancellationToken)
    {
        CurrentAccount = null;
        State = AuthenticationState.SignedOut;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Proves the ordering rule: no selected-user query can happen until both tenant locks pass.
/// </summary>
public sealed class CloudReadSessionTests
{
    private static readonly Guid ApprovedTenant = Guid.Parse("c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af");
    private static readonly Guid OtherTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AdGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    private static AdCorrelationSubject Subject(string? sid = null) => new()
    {
        ObjectGuid = AdGuid,
        SecurityIdentifier = sid,
        DistinguishedName = "CN=User,OU=Lab,DC=example,DC=com",
    };

    [Fact]
    public async Task No_account_means_signed_out_and_no_directory_read()
    {
        var fake = new FakeGraphProvider();
        var session = new CloudReadSession(new FakeAuthenticationService(), BuildProvider(fake), ApprovedTenant);

        var (tenant, result) = await session.EstablishTenantAsync(CancellationToken.None);

        Assert.Null(tenant);
        Assert.Equal(CloudSectionState.SignedOut, result.State);
        Assert.Equal(0, fake.OrganizationReads);
    }

    [Fact]
    public async Task T1_mismatch_blocks_the_directory_read_entirely()
    {
        var fake = new FakeGraphProvider();
        var auth = new FakeAuthenticationService
        {
            CurrentAccount = AuthenticatedAccount.Create(null, OtherTenant, "someone@other.example", true),
        };
        var session = new CloudReadSession(auth, BuildProvider(fake), ApprovedTenant);

        var (tenant, result) = await session.EstablishTenantAsync(CancellationToken.None);

        Assert.NotNull(tenant);
        Assert.False(tenant!.IsVerified);
        Assert.Equal(CloudSectionState.TenantMismatch, result.State);
        Assert.Equal(0, fake.OrganizationReads);
        Assert.False(fake.UserWasQueried);
    }

    [Fact]
    public async Task T2_mismatch_after_t1_match_blocks_user_queries()
    {
        var fake = new FakeGraphProvider { DirectoryTenantId = OtherTenant };
        var auth = new FakeAuthenticationService
        {
            CurrentAccount = AuthenticatedAccount.Create(null, ApprovedTenant, "admin@lab.example", true),
        };
        var session = new CloudReadSession(auth, BuildProvider(fake), ApprovedTenant);

        var (tenant, result) = await session.EstablishTenantAsync(CancellationToken.None);

        Assert.NotNull(tenant);
        Assert.True(tenant!.TokenTenantMatched);
        Assert.False(tenant.DirectoryTenantMatched);
        Assert.Equal(CloudSectionState.TenantMismatch, result.State);
        Assert.Equal(1, fake.OrganizationReads);
        Assert.False(fake.UserWasQueried);
    }

    [Fact]
    public async Task Both_locks_matching_reaches_tenant_verified_without_reading_a_user()
    {
        var fake = new FakeGraphProvider();
        var auth = new FakeAuthenticationService
        {
            CurrentAccount = AuthenticatedAccount.Create(null, ApprovedTenant, "admin@lab.example", true),
        };
        var session = new CloudReadSession(auth, BuildProvider(fake), ApprovedTenant);

        var (tenant, result) = await session.EstablishTenantAsync(CancellationToken.None);

        Assert.NotNull(tenant);
        Assert.True(tenant!.IsVerified);
        Assert.Equal(CloudSectionState.TenantVerified, result.State);
        Assert.False(result.ShowsUserData);
        Assert.False(fake.UserWasQueried);
    }

    [Fact]
    public async Task Correlating_with_a_failed_tenant_never_queries_the_directory()
    {
        var fake = new FakeGraphProvider();
        var session = new CloudReadSession(new FakeAuthenticationService(), BuildProvider(fake), ApprovedTenant);
        var failed = TenantVerificationResult.TokenMismatch(OtherTenant, ApprovedTenant);

        var result = await session.CorrelateAsync(Subject(), failed, consistencyGuid: null, CancellationToken.None);

        Assert.Equal(CloudSectionState.TenantMismatch, result.State);
        Assert.Null(result.User);
        Assert.False(fake.UserWasQueried);
    }

    [Fact]
    public async Task A_verified_lock_with_no_match_reports_unmatched_without_user_data()
    {
        var fake = new FakeGraphProvider();
        var auth = new FakeAuthenticationService
        {
            CurrentAccount = AuthenticatedAccount.Create(null, ApprovedTenant, "admin@lab.example", true),
        };
        var session = new CloudReadSession(auth, BuildProvider(fake), ApprovedTenant);
        var verified = TenantVerificationResult.Verified(ApprovedTenant, ApprovedTenant, ApprovedTenant);

        var result = await session.CorrelateAsync(Subject(), verified, consistencyGuid: null, CancellationToken.None);

        Assert.Equal(CloudSectionState.Unmatched, result.State);
        Assert.False(result.ShowsUserData);
        Assert.True(fake.UserWasQueried);
    }

    [Fact]
    public async Task A_verified_match_presents_the_cloud_user()
    {
        var fake = new FakeGraphProvider();
        var entraId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var candidate = Core.Correlation.ImmutableIdCandidate.FromObjectGuid(AdGuid).Value;
        fake.UsersByImmutableId[candidate] =
        [
            new CloudDirectoryUser
            {
                EntraObjectId = entraId,
                OnPremisesSecurityIdentifier = "S-1-5-21-1-2-3-1103",
                OnPremisesDistinguishedName = "CN=User,OU=Lab,DC=example,DC=com",
            },
        ];

        var auth = new FakeAuthenticationService
        {
            CurrentAccount = AuthenticatedAccount.Create(null, ApprovedTenant, "admin@lab.example", true),
        };
        var session = new CloudReadSession(auth, BuildProvider(fake), ApprovedTenant);
        var verified = TenantVerificationResult.Verified(ApprovedTenant, ApprovedTenant, ApprovedTenant);

        var result = await session.CorrelateAsync(Subject("S-1-5-21-1-2-3-1103"), verified, consistencyGuid: null, CancellationToken.None);

        Assert.Equal(CloudSectionState.Verified, result.State);
        Assert.True(result.ShowsUserData);
        Assert.Equal(entraId, result.User!.EntraObjectId);
        Assert.False(result.User.CanWrite);
    }

    [Fact]
    public async Task A_permission_failure_surfaces_as_a_missing_permission_rather_than_unmatched()
    {
        var fake = new FakeGraphProvider
        {
            UserQueryError = OperationError.Create(FailureCategory.Authorization, "Missing permission."),
        };
        var auth = new FakeAuthenticationService
        {
            CurrentAccount = AuthenticatedAccount.Create(null, ApprovedTenant, "admin@lab.example", true),
        };
        var session = new CloudReadSession(auth, BuildProvider(fake), ApprovedTenant);
        var verified = TenantVerificationResult.Verified(ApprovedTenant, ApprovedTenant, ApprovedTenant);

        var result = await session.CorrelateAsync(Subject(), verified, consistencyGuid: null, CancellationToken.None);

        Assert.Equal(CloudSectionState.PermissionMissing, result.State);
        Assert.False(result.ShowsUserData);
    }

    [Fact]
    public async Task A_catalogue_failure_leaves_the_verified_identity_intact()
    {
        var fake = new FakeGraphProvider { SkuError = OperationError.Create(FailureCategory.Authorization, "Missing.") };
        var entraId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var candidate = Core.Correlation.ImmutableIdCandidate.FromObjectGuid(AdGuid).Value;
        fake.UsersByImmutableId[candidate] =
        [
            new CloudDirectoryUser
            {
                EntraObjectId = entraId,
                Licenses = [new CloudLicenseAssignment(Guid.NewGuid(), null, null, null)],
            },
        ];

        var auth = new FakeAuthenticationService
        {
            CurrentAccount = AuthenticatedAccount.Create(null, ApprovedTenant, "admin@lab.example", true),
        };
        var session = new CloudReadSession(auth, BuildProvider(fake), ApprovedTenant);
        var verified = TenantVerificationResult.Verified(ApprovedTenant, ApprovedTenant, ApprovedTenant);

        var correlated = await session.CorrelateAsync(Subject(), verified, consistencyGuid: null, CancellationToken.None);
        var resolved = await session.ResolveLicensesAsync(correlated, CancellationToken.None);

        Assert.Equal(CloudSectionState.Verified, resolved.State);
        Assert.Equal(entraId, resolved.User!.EntraObjectId);
        Assert.False(resolved.User.LicenseCatalogueResolved);
    }

    [Fact]
    public async Task A_known_sku_is_named_from_the_tenant_catalogue()
    {
        var skuId = Guid.Parse("cccccccc-dddd-eeee-ffff-000000000000");
        var fake = new FakeGraphProvider();
        fake.Skus.Add(new CloudSubscribedSku { SkuId = skuId, SkuPartNumber = "LAB_SKU" });

        var candidate = Core.Correlation.ImmutableIdCandidate.FromObjectGuid(AdGuid).Value;
        fake.UsersByImmutableId[candidate] =
        [
            new CloudDirectoryUser
            {
                EntraObjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                Licenses = [new CloudLicenseAssignment(skuId, null, null, null)],
            },
        ];

        var auth = new FakeAuthenticationService
        {
            CurrentAccount = AuthenticatedAccount.Create(null, ApprovedTenant, "admin@lab.example", true),
        };
        var session = new CloudReadSession(auth, BuildProvider(fake), ApprovedTenant);

        var correlated = await session.CorrelateAsync(
            Subject(),
            TenantVerificationResult.Verified(ApprovedTenant, ApprovedTenant, ApprovedTenant),
            consistencyGuid: null,
            CancellationToken.None);
        var resolved = await session.ResolveLicensesAsync(correlated, CancellationToken.None);

        var license = Assert.Single(resolved.User!.Licenses);
        Assert.Equal("LAB_SKU", license.Label);
        Assert.False(license.IsUnresolved);
        Assert.True(resolved.User.LicenseCatalogueResolved);
    }

    [Fact]
    public async Task An_unknown_sku_shows_its_identifier_rather_than_a_guessed_name()
    {
        var skuId = Guid.Parse("cccccccc-dddd-eeee-ffff-111111111111");
        var fake = new FakeGraphProvider();
        var candidate = Core.Correlation.ImmutableIdCandidate.FromObjectGuid(AdGuid).Value;
        fake.UsersByImmutableId[candidate] =
        [
            new CloudDirectoryUser
            {
                EntraObjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                Licenses = [new CloudLicenseAssignment(skuId, null, null, null)],
            },
        ];

        var auth = new FakeAuthenticationService
        {
            CurrentAccount = AuthenticatedAccount.Create(null, ApprovedTenant, "admin@lab.example", true),
        };
        var session = new CloudReadSession(auth, BuildProvider(fake), ApprovedTenant);

        var correlated = await session.CorrelateAsync(
            Subject(),
            TenantVerificationResult.Verified(ApprovedTenant, ApprovedTenant, ApprovedTenant),
            consistencyGuid: null,
            CancellationToken.None);
        var resolved = await session.ResolveLicensesAsync(correlated, CancellationToken.None);

        var license = Assert.Single(resolved.User!.Licenses);
        Assert.True(license.IsUnresolved);
        Assert.Equal(skuId.ToString("D"), license.Label);
    }

    private static GraphCloudProvider BuildProvider(FakeGraphProvider fake) =>
        new(new DelegatingReadClient(fake), new FakeClock());
}

/// <summary>Adapts the call-recording fake to the narrow read seam.</summary>
internal sealed class DelegatingReadClient : Reads.IGraphReadClient
{
    private readonly FakeGraphProvider _inner;

    public DelegatingReadClient(FakeGraphProvider inner) => _inner = inner;

    public Task<ProviderResult<Guid>> ReadOrganizationIdAsync(CancellationToken cancellationToken) =>
        _inner.GetOrganizationTenantIdAsync(cancellationToken);

    public Task<ProviderResult<IReadOnlyList<CloudDirectoryUser>>> ReadUsersByImmutableIdAsync(
        string escapedImmutableId,
        CancellationToken cancellationToken) =>
        _inner.FindUsersByImmutableIdAsync(escapedImmutableId, cancellationToken);

    public Task<ProviderResult<IReadOnlyList<CloudSubscribedSku>>> ReadSubscribedSkusAsync(CancellationToken cancellationToken) =>
        _inner.GetSubscribedSkusAsync(cancellationToken);
}
