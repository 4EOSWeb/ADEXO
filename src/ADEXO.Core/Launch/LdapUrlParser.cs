using System.Security.Cryptography;
using System.Text;
using ADEXO.Contracts.Launch;

namespace ADEXO.Core.Launch;

public static class LdapUrlParser
{
    public static bool TryParse(string value, out ParsedLdapPath? path, out string? error)
    {
        path = null;
        error = null;
        const string prefix = "LDAP://";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "The path does not use the LDAP provider.";
            return false;
        }

        var remainder = value[prefix.Length..];
        if (remainder.Contains('?', StringComparison.Ordinal))
        {
            error = "The LDAP URL contains trailing material.";
            return false;
        }

        string? server = null;
        string distinguishedName;
        var slash = remainder.IndexOf('/');
        if (slash >= 0)
        {
            server = remainder[..slash];
            distinguishedName = remainder[(slash + 1)..];
            if (!IsAcceptableHostShape(server, out error))
            {
                return false;
            }
        }
        else
        {
            distinguishedName = remainder;
        }

        if (!DistinguishedNameParser.TryParse(distinguishedName, out var name, out error) || name is null)
        {
            return false;
        }

        path = new ParsedLdapPath("LDAP", server, name, Fingerprint(value));
        return true;
    }

    private static bool IsAcceptableHostShape(string server, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(server) || server.Contains(':', StringComparison.Ordinal) || server.Contains(' ', StringComparison.Ordinal))
        {
            error = "The LDAP host is malformed.";
            return false;
        }

        return true;
    }

    private static string Fingerprint(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..12];
    }
}
