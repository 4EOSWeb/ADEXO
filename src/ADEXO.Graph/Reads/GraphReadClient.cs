using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Results;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using OperationError = ADEXO.Contracts.Results.OperationError;

namespace ADEXO.Graph.Reads;

/// <summary>
/// The typed, read-only Graph request surface. This is the only place a Graph request is composed.
/// <para>
/// There is no method that accepts a URL, an HTTP method, an OData fragment, or a request body. The
/// provider above it exposes exactly the three operations Phase 3A needs, so a caller cannot widen the
/// request surface. Nothing in this type issues a create, update, or delete.
/// </para>
/// </summary>
public sealed class GraphReadClient : IGraphReadClient
{
    /// <summary>
    /// The exact properties Phase 3A reads. Every one was checked against the v1.0 user resource.
    /// Requesting a narrow set keeps the payload small and avoids pulling properties the phase does
    /// not use.
    /// </summary>
    internal const string UserSelect =
        "id,displayName,userPrincipalName,mail,accountEnabled,usageLocation," +
        "onPremisesSyncEnabled,onPremisesLastSyncDateTime,onPremisesImmutableId," +
        "onPremisesDistinguishedName,onPremisesSecurityIdentifier,onPremisesSamAccountName," +
        "proxyAddresses,assignedLicenses,assignedPlans";

    /// <summary>The tenant lock reads only the organization identifier.</summary>
    internal const string OrganizationSelect = "id";

    internal const string SkuSelect = "skuId,skuPartNumber,consumedUnits,prepaidUnits,servicePlans,appliesTo";

    private readonly GraphServiceClient _client;

    public GraphReadClient(GraphServiceClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>
    /// Reads the organization identifier for the tenant lock. This is the directory half of the lock,
    /// and it is available under the delegated <c>User.Read</c> scope.
    /// </summary>
    public async Task<ProviderResult<Guid>> ReadOrganizationIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            var organization = await _client.Organization
                .GetAsync(request => { request.QueryParameters.Select = [OrganizationSelect]; }, cancellationToken)
                .ConfigureAwait(false);

            var first = organization?.Value?.FirstOrDefault();
            if (first?.Id is null || !Guid.TryParse(first.Id, out var id))
            {
                return ProviderResult<Guid>.Failed(OperationError.Create(
                    FailureCategory.VerificationFailed,
                    "The organization read did not return a tenant identifier."));
            }

            return ProviderResult<Guid>.Success(id);
        }
        catch (OperationCanceledException)
        {
            return ProviderResult<Guid>.Canceled();
        }
        catch (ODataError error)
        {
            return ProviderResult<Guid>.Failed(Map(error));
        }
        catch (Exception exception)
        {
            return ProviderResult<Guid>.Failed(MapUnexpected(exception));
        }
    }

    /// <summary>
    /// Finds users whose synchronized immutable identifier equals the supplied value. The caller has
    /// already escaped the value; this method never accepts raw filter text.
    /// </summary>
    public async Task<ProviderResult<IReadOnlyList<CloudDirectoryUser>>> ReadUsersByImmutableIdAsync(
        string escapedImmutableId,
        CancellationToken cancellationToken)
    {
        try
        {
            var users = await _client.Users
                .GetAsync(
                    request =>
                    {
                        request.QueryParameters.Select = [UserSelect];
                        request.QueryParameters.Filter = $"onPremisesImmutableId eq '{escapedImmutableId}'";
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            var mapped = users?.Value is null
                ? new List<CloudDirectoryUser>()
                : users.Value.Select(MapUser).ToList();

            return ProviderResult<IReadOnlyList<CloudDirectoryUser>>.Success(mapped);
        }
        catch (OperationCanceledException)
        {
            return ProviderResult<IReadOnlyList<CloudDirectoryUser>>.Canceled();
        }
        catch (ODataError error)
        {
            return ProviderResult<IReadOnlyList<CloudDirectoryUser>>.Failed(Map(error));
        }
        catch (Exception exception)
        {
            return ProviderResult<IReadOnlyList<CloudDirectoryUser>>.Failed(MapUnexpected(exception));
        }
    }

    /// <summary>Reads the tenant SKU catalogue. Used only to name licences the user already holds.</summary>
    public async Task<ProviderResult<IReadOnlyList<CloudSubscribedSku>>> ReadSubscribedSkusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var skus = await _client.SubscribedSkus
                .GetAsync(
                    request => { request.QueryParameters.Select = [SkuSelect]; },
                    cancellationToken)
                .ConfigureAwait(false);

            var mapped = skus?.Value is null
                ? new List<CloudSubscribedSku>()
                : skus.Value.Select(sku => new CloudSubscribedSku
                {
                    SkuId = Guid.TryParse(sku.SkuId?.ToString(), out var id) ? id : Guid.Empty,
                    SkuPartNumber = sku.SkuPartNumber,
                    DisplayName = null,
                    ConsumedUnits = sku.ConsumedUnits,
                    EnabledUnits = sku.PrepaidUnits?.Enabled,
                    SuspendedUnits = sku.PrepaidUnits?.Suspended,
                    WarningUnits = sku.PrepaidUnits?.Warning,
                }).ToList();

            return ProviderResult<IReadOnlyList<CloudSubscribedSku>>.Success(mapped);
        }
        catch (OperationCanceledException)
        {
            return ProviderResult<IReadOnlyList<CloudSubscribedSku>>.Canceled();
        }
        catch (ODataError error)
        {
            return ProviderResult<IReadOnlyList<CloudSubscribedSku>>.Failed(Map(error));
        }
        catch (Exception exception)
        {
            return ProviderResult<IReadOnlyList<CloudSubscribedSku>>.Failed(MapUnexpected(exception));
        }
    }

    private static CloudDirectoryUser MapUser(User user) => new()
    {
        EntraObjectId = Guid.TryParse(user.Id, out var id) ? id : Guid.Empty,
        DisplayName = user.DisplayName,
        UserPrincipalName = user.UserPrincipalName,
        Mail = user.Mail,
        AccountEnabled = user.AccountEnabled,
        UsageLocation = user.UsageLocation,
        OnPremisesSyncEnabled = user.OnPremisesSyncEnabled,
        OnPremisesLastSyncDateTime = user.OnPremisesLastSyncDateTime,
        OnPremisesImmutableId = user.OnPremisesImmutableId,
        OnPremisesDistinguishedName = user.OnPremisesDistinguishedName,
        OnPremisesSecurityIdentifier = user.OnPremisesSecurityIdentifier,
        OnPremisesSamAccountName = user.OnPremisesSamAccountName,
        ProxyAddresses = user.ProxyAddresses?.ToList() ?? [],
        Licenses = MapLicenses(user),
    };

    private static IReadOnlyList<CloudLicenseAssignment> MapLicenses(User user)
    {
        var assigned = user.AssignedLicenses?
            .Where(license => license.SkuId is not null)
            .Select(license => license.SkuId!.Value)
            .Distinct()
            .ToList() ?? [];

        // Names are resolved later against the tenant SKU catalogue. Until then a licence carries only
        // its identifier, which the UI shows rather than inventing a product name.
        return assigned
            .Select(skuId => new CloudLicenseAssignment(skuId, skuPartNumber: null, displayName: null, servicePlanSummary: null))
            .ToList();
    }

    /// <summary>
    /// Maps a Graph error to the existing failure vocabulary so a missing permission is reported as an
    /// authorization failure rather than as "user not found".
    /// </summary>
    private static OperationError Map(ODataError error)
    {
        var status = error.ResponseStatusCode;
        var category = status switch
        {
            401 => FailureCategory.Authentication,
            403 => FailureCategory.Authorization,
            404 => FailureCategory.NotFound,
            408 => FailureCategory.Timeout,
            429 => FailureCategory.Throttled,
            >= 500 => FailureCategory.ProviderUnavailable,
            _ => FailureCategory.Unknown,
        };

        var summary = category switch
        {
            FailureCategory.Authentication => "Microsoft Graph rejected the session. Sign in again.",
            FailureCategory.Authorization => "Microsoft Graph refused the request because a permission is missing.",
            FailureCategory.NotFound => "Microsoft Graph did not find the requested object.",
            FailureCategory.Timeout => "Microsoft Graph did not respond in time.",
            FailureCategory.Throttled => "Microsoft Graph throttled the request. Try again shortly.",
            FailureCategory.ProviderUnavailable => "Microsoft Graph is currently unavailable.",
            _ => "Microsoft Graph returned an error.",
        };

        return OperationError.Create(category, summary);
    }

    private static OperationError MapUnexpected(Exception exception) =>
        exception is TaskCanceledException
            ? OperationError.Create(FailureCategory.Timeout, "Microsoft Graph did not respond in time.")
            : OperationError.Create(FailureCategory.ProviderUnavailable, "Microsoft Graph could not be reached.");
}
