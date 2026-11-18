namespace ADEXO.Contracts.Directory;

public sealed class TrustedDirectoryEndpoint
{
    public TrustedDirectoryEndpoint(
        string hostName,
        string defaultNamingContext,
        string configurationNamingContext,
        string? rootDomainNamingContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultNamingContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationNamingContext);
        HostName = hostName;
        DefaultNamingContext = defaultNamingContext;
        ConfigurationNamingContext = configurationNamingContext;
        RootDomainNamingContext = rootDomainNamingContext;
    }

    public string HostName { get; }

    public string DefaultNamingContext { get; }

    public string ConfigurationNamingContext { get; }

    public string? RootDomainNamingContext { get; }
}

public sealed class EndpointSelection
{
    private EndpointSelection(bool accepted, TrustedDirectoryEndpoint? endpoint, string? failureCode, string? failureMessage)
    {
        Accepted = accepted;
        Endpoint = endpoint;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
    }

    public bool Accepted { get; }

    public TrustedDirectoryEndpoint? Endpoint { get; }

    public string? FailureCode { get; }

    public string? FailureMessage { get; }

    public static EndpointSelection Success(TrustedDirectoryEndpoint endpoint) => new(true, endpoint, null, null);

    public static EndpointSelection Reject(string code, string message) => new(false, null, code, message);
}
