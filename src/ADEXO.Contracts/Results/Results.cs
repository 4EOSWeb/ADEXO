namespace ADEXO.Contracts.Results;

public enum FailureCategory
{
    Authentication,
    Authorization,
    Validation,
    NotFound,
    AmbiguousMatch,
    ConcurrencyConflict,
    Network,
    Timeout,
    Throttled,
    ProviderUnavailable,
    Unsupported,
    PartialCompletion,
    VerificationFailed,
    Unknown,
}

public sealed class OperationError
{
    private OperationError(FailureCategory category, string summary, Exception? exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        Category = category;
        Summary = summary;
        Exception = exception;
    }

    public FailureCategory Category { get; }

    public string Summary { get; }

    internal Exception? Exception { get; }

    public string DiagnosticDetail => Exception is null ? Summary : Exception.GetType().Name;

    public static OperationError Create(FailureCategory category, string summary) => new(category, summary, null);

    public static OperationError Unexpected(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is OperationCanceledException)
        {
            throw new ArgumentException("Cancellation is not an unknown failure.", nameof(exception));
        }

        return new OperationError(FailureCategory.Unknown, exception.GetType().Name, exception);
    }
}

public sealed class OperationResult<T>
{
    private OperationResult(bool canceled, bool success, T? value, OperationError? error)
    {
        IsCanceled = canceled;
        IsSuccess = success;
        Value = value;
        Error = error;
    }

    public bool IsCanceled { get; }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public OperationError? Error { get; }

    public static OperationResult<T> Success(T value) => new(false, true, value, null);

    public static OperationResult<T> Canceled() => new(true, false, default, null);

    public static OperationResult<T> Failed(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new OperationResult<T>(false, false, default, error);
    }

    public static OperationResult<T> FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is OperationCanceledException)
        {
            return Canceled();
        }

        return Failed(OperationError.Unexpected(exception));
    }
}

public sealed class ProviderResult<T>
{
    private ProviderResult(bool canceled, bool success, T? value, OperationError? error)
    {
        IsCanceled = canceled;
        IsSuccess = success;
        Value = value;
        Error = error;
    }

    public bool IsCanceled { get; }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public OperationError? Error { get; }

    public static ProviderResult<T> Success(T value) => new(false, true, value, null);

    public static ProviderResult<T> Canceled() => new(true, false, default, null);

    public static ProviderResult<T> Failed(OperationError error) => new(false, false, default, error ?? throw new ArgumentNullException(nameof(error)));
}

public sealed record VerificationResult(bool Confirmed, string Summary);

public sealed record RollbackResult(bool Completed, string Summary);
