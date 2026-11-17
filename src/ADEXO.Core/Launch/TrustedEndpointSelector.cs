using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Launch;

namespace ADEXO.Core.Launch;

public static class TrustedEndpointSelector
{
    public static EndpointSelection Select(ParsedLdapPath path, TrustedDirectoryEndpoint discovered)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(discovered);
        if (!NameContextContains(path.DistinguishedName.Normalized, discovered.DefaultNamingContext))
        {
            return EndpointSelection.Reject("naming-context", "The distinguished name is outside the discovered domain.");
        }

        if (path.Server is null)
        {
            return EndpointSelection.Success(discovered);
        }

        if (IsIpLiteral(path.Server))
        {
            return EndpointSelection.Reject("ip-literal", "An IP address cannot select the directory server.");
        }

        if (!path.Server.Contains('.', StringComparison.Ordinal))
        {
            return EndpointSelection.Reject("unqualified-host", "An unqualified host is not trusted.");
        }

        if (!path.Server.Equals(discovered.HostName, StringComparison.OrdinalIgnoreCase))
        {
            return EndpointSelection.Reject("untrusted-host", "The path names a server that discovery did not return.");
        }

        return EndpointSelection.Success(discovered);
    }

    private static bool NameContextContains(string distinguishedName, string namingContext) =>
        distinguishedName.EndsWith(namingContext, StringComparison.OrdinalIgnoreCase);

    private static bool IsIpLiteral(string host)
    {
        var parts = host.Split('.');
        return parts.Length == 4 && parts.All(part => byte.TryParse(part, out _));
    }
}
