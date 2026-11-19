using ADEXO.Contracts.Authentication;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Results;
using ADEXO.Core.Correlation;
using ADEXO.Graph.Authentication;

namespace ADEXO.Graph.Orchestration;

/// <summary>
/// The state the cloud section displays. Every value after <see cref="NotConfigured"/> is reachable
/// without touching Active Directory state, which is what keeps the two views independent.
/// </summary>
public enum CloudSectionState
{
    NotConfigured,
    SignedOut,
    SigningIn,
    Authenticated,
    TenantVerified,
    TenantMismatch,
    CorrelationPending,
    Verified,
    Unmatched,
    Ambiguous,
    Conflict,
    GraphUnavailable,
    PermissionMissing,
    Canceled,
    Failed,
}

/// <summary>The complete read-only cloud result for one AD selection.</summary>
public sealed class CloudSectionResult
{
    public required CloudSectionState State { get; init; }

    public AuthenticatedAccount? Account { get; init; }

    public TenantVerificationResult? Tenant { get; init; }

    public CloudCorrelationResult? Correlation { get; init; }

    public CloudDirectoryUser? User { get; init; }

    public string? Message { get; init; }

    /// <summary>True only when a correlated cloud user may be presented as this AD object's cloud state.</summary>
    public bool ShowsUserData => State == CloudSectionState.Verified && User is not null;

    public static CloudSectionResult Of(CloudSectionState state, string? message = null, AuthenticatedAccount? account = null) =>
        new() { State = state, Message = message, Account = account };
}

/// <summary>
/// Runs the Phase 3A chain in order: authenticate, T1, T2, then correlate.
/// <para>
/// The ordering is not a convention here, it is the only path through this class. Correlation cannot be
/// reached without a verified tenant result, and the tenant result cannot be forged because its
/// constructors are private and only this chain produces one.
/// </para>
/// </summary>
public sealed class CloudReadSession
{
    private readonly IMicrosoftAuthenticationService _authentication;
    private readonly GraphCloudProvider _provider;
    private readonly Guid _approvedTenantId;

    public CloudReadSession(
        IMicrosoftAuthenticationService authentication,
        GraphCloudProvider provider,
        Guid approvedTenantId)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));

        if (approvedTenantId == Guid.Empty)
        {
            throw new ArgumentException("An approved tenant identifier is required.", nameof(approvedTenantId));
        }

        _approvedTenantId = approvedTenantId;
    }

    /// <summary>The approved tenant this session locks to. Configuration may not silently override it.</summary>
    public Guid ApprovedTenantId => _approvedTenantId;

    /// <summary>
    /// Completes the sign-in and both tenant locks, without reading any selected user. This is the
    /// state the section shows when it says the tenant is verified but no correlation has been made.
    /// </summary>
    public async Task<(TenantVerificationResult? Tenant, CloudSectionResult Result)> EstablishTenantAsync(CancellationToken cancellationToken)
    {
        var account = _authentication.CurrentAccount;
        if (account is null)
        {
            return (null, CloudSectionResult.Of(CloudSectionState.SignedOut));
        }

        // T1: the token tenant, checked before anything is requested from the directory.
        if (account.TenantId != _approvedTenantId)
        {
            var mismatch = TenantVerificationResult.TokenMismatch(account.TenantId, _approvedTenantId);
            return (mismatch, new CloudSectionResult
            {
                State = CloudSectionState.TenantMismatch,
                Tenant = mismatch,
                Account = account,
                Message = mismatch.Summary,
            });
        }

        // T2: the directory tenant, read with the narrowest possible request.
        ProviderResult<Guid> organization;
        try
        {
            organization = await _provider.GetOrganizationTenantIdAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return (null, CloudSectionResult.Of(CloudSectionState.Canceled, "The tenant check was canceled.", account));
        }

        if (organization.IsCanceled)
        {
            return (null, CloudSectionResult.Of(CloudSectionState.Canceled, "The tenant check was canceled.", account));
        }

        if (!organization.IsSuccess || organization.Value == Guid.Empty)
        {
            var unavailable = TenantVerificationResult.Unavailable(
                account.TenantId,
                _approvedTenantId,
                organization.Error?.Summary ?? "The tenant could not be verified.");

            return (unavailable, new CloudSectionResult
            {
                State = MapFailure(organization.Error),
                Tenant = unavailable,
                Account = account,
                Message = organization.Error?.Summary,
            });
        }

        if (organization.Value != _approvedTenantId)
        {
            // T1 held but the directory disagreed. That is an identity failure, so it is surfaced as a
            // lock failure rather than as a transient error, and no user query follows.
            var mismatch = TenantVerificationResult.DirectoryMismatch(account.TenantId, organization.Value, _approvedTenantId);
            return (mismatch, new CloudSectionResult
            {
                State = CloudSectionState.TenantMismatch,
                Tenant = mismatch,
                Account = account,
                Message = mismatch.Summary,
            });
        }

        var verified = TenantVerificationResult.Verified(account.TenantId, organization.Value, _approvedTenantId);
        return (verified, new CloudSectionResult
        {
            State = CloudSectionState.TenantVerified,
            Tenant = verified,
            Account = account,
            Message = "The tenant is verified. No cloud user has been read yet.",
        });
    }

    /// <summary>
    /// Correlates the selected AD object and, when the outcome is verified, resolves its licence names.
    /// Any state other than Verified deliberately leaves <see cref="CloudSectionResult.User"/> null.
    /// </summary>
    public async Task<CloudSectionResult> CorrelateAsync(
        AdCorrelationSubject subject,
        TenantVerificationResult tenant,
        byte[]? consistencyGuid,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(tenant);

        if (!tenant.IsVerified)
        {
            return new CloudSectionResult
            {
                State = CloudSectionState.TenantMismatch,
                Tenant = tenant,
                Account = _authentication.CurrentAccount,
                Message = tenant.Summary,
            };
        }

        CloudCorrelationResult correlation;
        CloudDirectoryUser? user = null;
        try
        {
            (correlation, user) = await _provider
                .CorrelateAsync(subject, tenant, consistencyGuid, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new CloudSectionResult
            {
                State = CloudSectionState.Canceled,
                Tenant = tenant,
                Account = _authentication.CurrentAccount,
                Message = "The cloud read was canceled.",
            };
        }
        catch (GraphReadException exception)
        {
            return new CloudSectionResult
            {
                State = MapFailure(exception.Error),
                Tenant = tenant,
                Account = _authentication.CurrentAccount,
                Message = exception.Error.Summary,
            };
        }

        if (!correlation.IsVerified || correlation.EntraObjectId is null)
        {
            return new CloudSectionResult
            {
                State = MapCorrelation(correlation.Outcome),
                Tenant = tenant,
                Correlation = correlation,
                Account = _authentication.CurrentAccount,
                Message = correlation.Summary,
            };
        }

        return new CloudSectionResult
        {
            State = CloudSectionState.Verified,
            Tenant = tenant,
            Correlation = correlation,
            Account = _authentication.CurrentAccount,
            User = user,
            Message = correlation.Summary,
        };
    }

    /// <summary>
    /// Applies the tenant SKU catalogue to a verified user's licences. A catalogue failure leaves the
    /// identity result untouched, so a licensing problem never erases a confirmed match.
    /// </summary>
    public async Task<CloudSectionResult> ResolveLicensesAsync(CloudSectionResult verified, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verified);

        if (!verified.ShowsUserData || verified.User is null)
        {
            return verified;
        }

        var user = await _provider.ResolveLicenseNamesAsync(verified.User, cancellationToken).ConfigureAwait(false);

        return new CloudSectionResult
        {
            State = verified.State,
            Tenant = verified.Tenant,
            Correlation = verified.Correlation,
            Account = verified.Account,
            Message = verified.Message,
            User = user,
        };
    }

    private static CloudSectionState MapCorrelation(CorrelationOutcome outcome) => outcome switch
    {
        CorrelationOutcome.Verified => CloudSectionState.Verified,
        CorrelationOutcome.Unmatched => CloudSectionState.Unmatched,
        CorrelationOutcome.Ambiguous => CloudSectionState.Ambiguous,
        CorrelationOutcome.Conflict => CloudSectionState.Conflict,
        CorrelationOutcome.TenantMismatch => CloudSectionState.TenantMismatch,
        _ => CloudSectionState.CorrelationPending,
    };

    private static CloudSectionState MapFailure(OperationError? error) => error?.Category switch
    {
        FailureCategory.Authorization => CloudSectionState.PermissionMissing,
        FailureCategory.Authentication => CloudSectionState.SignedOut,
        FailureCategory.Throttled => CloudSectionState.Failed,
        FailureCategory.Network or FailureCategory.ProviderUnavailable or FailureCategory.Timeout => CloudSectionState.GraphUnavailable,
        FailureCategory.NotFound => CloudSectionState.Unmatched,
        _ => CloudSectionState.Failed,
    };
}
