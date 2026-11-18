namespace ADEXO.Contracts.Launch;

public enum LaunchSource
{
    AducStaticContextMenu,
    DirectCommandLine,
    Unknown,
}

public sealed class AducLaunchRequest
{
    public AducLaunchRequest(ParsedLdapPath path, string objectClass, LaunchSource source, DateTimeOffset receivedUtc)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectClass);
        Path = path;
        ObjectClass = objectClass;
        Source = source;
        ReceivedUtc = receivedUtc;
    }

    public ParsedLdapPath Path { get; }

    public string ObjectClass { get; }

    public LaunchSource Source { get; }

    public DateTimeOffset ReceivedUtc { get; }
}

public sealed class LaunchParseOutcome
{
    private LaunchParseOutcome(bool empty, AducLaunchRequest? request, string? code, string? message)
    {
        IsEmptyLaunch = empty;
        Request = request;
        FailureCode = code;
        FailureMessage = message;
    }

    public bool IsEmptyLaunch { get; }

    public bool Succeeded => Request is not null;

    public AducLaunchRequest? Request { get; }

    public string? FailureCode { get; }

    public string? FailureMessage { get; }

    public static LaunchParseOutcome Empty() => new(true, null, null, null);

    public static LaunchParseOutcome Success(AducLaunchRequest request) => new(false, request, null, null);

    public static LaunchParseOutcome Failure(string code, string message) => new(false, null, code, message);
}
