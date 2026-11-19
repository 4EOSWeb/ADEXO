namespace ADEXO.Contracts.Launch;

public sealed class DistinguishedName
{
    private DistinguishedName(string normalized)
    {
        Normalized = normalized;
    }

    public string Normalized { get; }

    public static DistinguishedName FromValidated(string normalized)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalized);
        return new DistinguishedName(normalized);
    }
}

public sealed class ParsedLdapPath
{
    public ParsedLdapPath(string provider, string? server, DistinguishedName distinguishedName, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(distinguishedName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        Provider = provider;
        Server = server;
        DistinguishedName = distinguishedName;
        Fingerprint = fingerprint;
    }

    public string Provider { get; }

    public string? Server { get; }

    public DistinguishedName DistinguishedName { get; }

    public string Fingerprint { get; }
}
