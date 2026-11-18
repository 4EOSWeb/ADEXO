namespace ADEXO.Contracts.Validation;

public enum ValidationSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record ValidationMessage(
    ValidationSeverity Severity,
    string Code,
    string Message,
    string? Property,
    bool Blocking);

public sealed class ValidationResult
{
    public ValidationResult(IReadOnlyList<ValidationMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        Messages = messages;
    }

    public IReadOnlyList<ValidationMessage> Messages { get; }

    public bool HasBlockingErrors => Messages.Any(message => message.Blocking || message.Severity == ValidationSeverity.Error);

    public bool HasWarnings => Messages.Any(message => message.Severity == ValidationSeverity.Warning);
}
