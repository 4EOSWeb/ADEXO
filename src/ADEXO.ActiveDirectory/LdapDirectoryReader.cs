using System.DirectoryServices.Protocols;
using System.Net;
using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Launch;
using ADEXO.Contracts.Results;
using ADEXO.ActiveDirectory.Ldap;

namespace ADEXO.ActiveDirectory;

public sealed class LdapDirectoryReader : IDirectoryReader
{
    public static readonly string[] UserAttributes =
    [
        "objectGUID", "objectSid", "distinguishedName", "canonicalName", "objectClass", "name",
        "displayName", "givenName", "sn", "initials", "sAMAccountName", "userPrincipalName",
        "userAccountControl", "accountExpires", "pwdLastSet", "lockoutTime", "badPwdCount",
        "whenCreated", "whenChanged", "mail", "telephoneNumber", "mobile", "ipPhone", "streetAddress",
        "l", "st", "postalCode", "co", "title", "department", "company", "physicalDeliveryOfficeName",
        "manager", "mailNickname", "proxyAddresses", "targetAddress", "msExchHideFromAddressLists",
        "extensionAttribute1", "extensionAttribute2", "extensionAttribute3", "extensionAttribute4",
        "extensionAttribute5", "extensionAttribute6", "extensionAttribute7", "extensionAttribute8",
        "extensionAttribute9", "extensionAttribute10", "extensionAttribute11", "extensionAttribute12",
        "extensionAttribute13", "extensionAttribute14", "extensionAttribute15",
    ];

    private readonly LdapConnectionSecurity _security;

    public LdapDirectoryReader(TimeSpan timeout)
    {
        _security = new LdapConnectionSecurity(timeout);
    }

    public Task<ProviderResult<TrustedDirectoryEndpoint>> ReadRootDseAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Execute(null, string.Empty, ["defaultNamingContext", "configurationNamingContext", "rootDomainNamingContext", "dnsHostName"], cancellationToken, MapRoot));

    public Task<ProviderResult<DirectoryAttributeBag>> ReadByDistinguishedNameAsync(
        TrustedDirectoryEndpoint endpoint,
        DistinguishedName distinguishedName,
        CancellationToken cancellationToken) =>
        Task.FromResult(Execute(endpoint.HostName, distinguishedName.Normalized, UserAttributes, cancellationToken, MapBag));

    public Task<ProviderResult<DirectoryAttributeBag>> ReadByObjectGuidAsync(
        TrustedDirectoryEndpoint endpoint,
        Guid objectGuid,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(ProviderResult<DirectoryAttributeBag>.Canceled());
        }

        var filter = LdapFilter.ObjectGuidFilter(objectGuid);
        return Task.FromResult(Search(endpoint.HostName, endpoint.DefaultNamingContext, filter, cancellationToken));
    }

    private ProviderResult<DirectoryAttributeBag> Search(string host, string namingContext, string filter, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ProviderResult<DirectoryAttributeBag>.Canceled();
        }

        try
        {
            using var connection = Open(host);
            var request = new SearchRequest(namingContext, filter, SearchScope.Subtree, UserAttributes)
            {
                TimeLimit = _security.Timeout,
            };
            var response = (SearchResponse)connection.SendRequest(request);
            if (response.Entries.Count == 0)
            {
                return ProviderResult<DirectoryAttributeBag>.Failed(OperationError.Create(FailureCategory.NotFound, "The object was not found."));
            }

            if (response.Entries.Count > 1)
            {
                return ProviderResult<DirectoryAttributeBag>.Failed(OperationError.Create(FailureCategory.AmbiguousMatch, "The object identifier matched more than one entry."));
            }

            return ProviderResult<DirectoryAttributeBag>.Success(ReadEntry(response.Entries[0]));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ProviderResult<DirectoryAttributeBag>.Failed(MapException(exception));
        }
    }

    private ProviderResult<T> Execute<T>(
        string? host,
        string distinguishedName,
        string[] attributes,
        CancellationToken cancellationToken,
        Func<SearchResultEntry, T> map)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ProviderResult<T>.Canceled();
        }

        try
        {
            using var connection = Open(host);
            var request = new SearchRequest(distinguishedName, "(objectClass=*)", SearchScope.Base, attributes)
            {
                TimeLimit = _security.Timeout,
            };
            var response = (SearchResponse)connection.SendRequest(request);
            if (response.Entries.Count != 1)
            {
                return ProviderResult<T>.Failed(OperationError.Create(FailureCategory.NotFound, "The directory object was not found."));
            }

            return ProviderResult<T>.Success(map(response.Entries[0]));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ProviderResult<T>.Failed(MapException(exception));
        }
    }

    private LdapConnection Open(string? host)
    {
        var identifier = string.IsNullOrWhiteSpace(host)
            ? new LdapDirectoryIdentifier((string?)null)
            : new LdapDirectoryIdentifier(host);
        var connection = new LdapConnection(identifier)
        {
            Credential = CredentialCache.DefaultNetworkCredentials,
        };
        try
        {
            _security.Apply(connection);
            connection.Bind();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static TrustedDirectoryEndpoint MapRoot(SearchResultEntry entry)
    {
        var host = First(entry, "dnsHostName") ?? "discovered-domain-controller";
        return new TrustedDirectoryEndpoint(
            host,
            First(entry, "defaultNamingContext") ?? throw new InvalidOperationException("defaultNamingContext was missing."),
            First(entry, "configurationNamingContext") ?? throw new InvalidOperationException("configurationNamingContext was missing."),
            First(entry, "rootDomainNamingContext"));
    }

    private static DirectoryAttributeBag MapBag(SearchResultEntry entry) => ReadEntry(entry);

    private static DirectoryAttributeBag ReadEntry(SearchResultEntry entry)
    {
        var text = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var binary = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in entry.Attributes.AttributeNames)
        {
            var attribute = entry.Attributes[name];
            if (attribute.Count == 0)
            {
                continue;
            }

            if (attribute[0] is byte[] bytes)
            {
                binary[name] = bytes;
                continue;
            }

            var values = new List<string>();
            foreach (var item in attribute.GetValues(typeof(string)))
            {
                if (item is string textValue)
                {
                    values.Add(textValue);
                }
            }

            text[name] = values;
        }

        if (!text.ContainsKey("distinguishedName"))
        {
            text["distinguishedName"] = [entry.DistinguishedName];
        }

        return new DirectoryAttributeBag(text, binary);
    }

    private static string? First(SearchResultEntry entry, string name)
    {
        if (!entry.Attributes.Contains(name))
        {
            return null;
        }

        var values = entry.Attributes[name].GetValues(typeof(string));
        return values.Length == 0 ? null : values[0] as string;
    }

    private static OperationError MapException(Exception exception)
    {
        var message = exception.GetType().Name;
        var category = exception switch
        {
            LdapException ldap when ldap.ErrorCode is 49 => FailureCategory.Authentication,
            LdapException ldap when ldap.ErrorCode is 50 or 32 => FailureCategory.Authorization,
            LdapException ldap when ldap.ErrorCode is 81 or 91 => FailureCategory.Network,
            LdapException ldap when ldap.ErrorCode is 85 or 3 => FailureCategory.Timeout,
            TimeoutException => FailureCategory.Timeout,
            _ => FailureCategory.ProviderUnavailable,
        };
        return OperationError.Create(category, message);
    }
}
