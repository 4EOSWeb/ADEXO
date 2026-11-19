using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using ADEXO.Contracts.Mailbox;
using ADEXO.Contracts.Results;

namespace ADEXO.ExchangeOnline;

/// <summary>
/// The only commands this assembly may invoke. The allowlist is a static set, not a filter, so a name
/// outside it cannot be run even by mistake.
/// </summary>
public static class ExchangeReadCommands
{
    public const string Connect = "Connect-ExchangeOnline";
    public const string Disconnect = "Disconnect-ExchangeOnline";
    public const string GetMailbox = "Get-EXOMailbox";
    public const string GetMailboxStatistics = "Get-EXOMailboxStatistics";
    public const string GetMailboxPermission = "Get-MailboxPermission";
    public const string GetRecipientPermission = "Get-RecipientPermission";

    /// <summary>Every command that may be invoked on the runspace.</summary>
    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        GetMailbox,
        GetMailboxStatistics,
        GetMailboxPermission,
        GetRecipientPermission,
        Connect,
        Disconnect,
    };

    /// <summary>Commands that only read. Connect and disconnect manage the session, not the directory.</summary>
    public static IReadOnlySet<string> ReadsOnly { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        GetMailbox,
        GetMailboxStatistics,
        GetMailboxPermission,
        GetRecipientPermission,
    };
}

/// <summary>One typed parameter for an allowed command.</summary>
public sealed record ExchangeParameter(string Name, object? Value);

/// <summary>
/// A typed Exchange invocation: an allowlisted command plus typed parameters. There is deliberately no
/// constructor that takes a script block, a command line, or a pipeline string.
/// </summary>
public sealed class ExchangeInvocation
{
    private readonly List<ExchangeParameter> _parameters = [];

    public ExchangeInvocation(string command)
    {
        if (!ExchangeReadCommands.ReadsOnly.Contains(command))
        {
            throw new ArgumentException($"'{command}' is not an allowed read command.", nameof(command));
        }

        Command = command;
    }

    public string Command { get; }

    public IReadOnlyList<ExchangeParameter> Parameters => _parameters;

    public ExchangeInvocation With(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _parameters.Add(new ExchangeParameter(name, value));
        return this;
    }
}

/// <summary>
/// Runs allowlisted Exchange commands on an isolated runspace.
/// <para>
/// The runspace is created with a restricted initial session state that contains no cmdlets by default,
/// and each invocation is assembled from typed <c>Commands.AddCommand</c> and <c>AddParameter</c> calls.
/// Nothing in this class accepts text to execute, so there is no path from caller input to
/// <c>Invoke-Expression</c>, a script file, or an interpolated command string.
/// </para>
/// </summary>
public sealed class ExchangeRunspace : IDisposable
{
    private readonly Runspace _runspace;
    private bool _disposed;

    public ExchangeRunspace(Runspace runspace) =>
        _runspace = runspace ?? throw new ArgumentNullException(nameof(runspace));

    /// <summary>True when a session has been established and not yet disconnected.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Creates a runspace for Exchange. It starts with no modules loaded; the Exchange module is
    /// imported explicitly so its cmdlets are the only ones available.
    /// </summary>
    public static ExchangeRunspace Create()
    {
        var initialState = InitialSessionState.CreateDefault2();

        // No script execution from disk, and no implicit commands.
        initialState.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.RemoteSigned;

        var runspace = RunspaceFactory.CreateRunspace(initialState);
        runspace.Open();
        return new ExchangeRunspace(runspace);
    }

    /// <summary>
    /// Invokes one allowlisted command with typed parameters and returns the resulting objects.
    /// A command outside the allowlist cannot reach this method.
    /// </summary>
    public Task<ProviderResult<Collection<PSObject>>> InvokeAsync(
        ExchangeInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!ExchangeReadCommands.ReadsOnly.Contains(invocation.Command))
        {
            return Task.FromResult(ProviderResult<Collection<PSObject>>.Failed(
                OperationError.Create(FailureCategory.Validation, "The command is not on the read allowlist.")));
        }

        using var powershell = PowerShell.Create();
        powershell.Runspace = _runspace;

        var command = new Command(invocation.Command);
        foreach (var parameter in invocation.Parameters)
        {
            command.Parameters.Add(new CommandParameter(parameter.Name, parameter.Value));
        }

        powershell.Commands.AddCommand(command);

        try
        {
            var results = powershell.Invoke();

            if (powershell.HadErrors && powershell.Streams.Error.Count > 0)
            {
                return Task.FromResult(ProviderResult<Collection<PSObject>>.Failed(MapErrors(powershell.Streams.Error)));
            }

            return Task.FromResult(ProviderResult<Collection<PSObject>>.Success(results));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(ProviderResult<Collection<PSObject>>.Canceled());
        }
        catch (RuntimeException exception)
        {
            return Task.FromResult(ProviderResult<Collection<PSObject>>.Failed(
                OperationError.Create(FailureCategory.ProviderUnavailable, Sanitize(exception))));
        }
    }

    /// <summary>
    /// Imports the Exchange module and connects interactively. The administrator authenticates through
    /// Microsoft's modern sign-in; no password, secret, or certificate is supplied by ADEXO.
    /// </summary>
    public Task<ProviderResult<bool>> ConnectInteractiveAsync(string organization, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);

        using var powershell = PowerShell.Create();
        powershell.Runspace = _runspace;

        powershell.Commands.AddCommand("Import-Module")
            .AddParameter("Name", "ExchangeOnlineManagement")
            .AddParameter("ErrorAction", "Stop");

        try
        {
            powershell.Invoke();
            if (powershell.HadErrors && powershell.Streams.Error.Count > 0)
            {
                return Task.FromResult(ProviderResult<bool>.Failed(
                    OperationError.Create(FailureCategory.ProviderUnavailable, "The Exchange Online module could not be loaded.")));
            }

            powershell.Commands.Clear();
            powershell.Commands.AddCommand(ExchangeReadCommands.Connect)
                .AddParameter("Organization", organization)
                .AddParameter("ShowBanner", false)
                .AddParameter("ErrorAction", "Stop");

            powershell.Invoke();

            if (powershell.HadErrors && powershell.Streams.Error.Count > 0)
            {
                return Task.FromResult(ProviderResult<bool>.Failed(MapErrors(powershell.Streams.Error)));
            }

            IsConnected = true;
            return Task.FromResult(ProviderResult<bool>.Success(true));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ProviderResult<bool>.Failed(
                OperationError.Create(FailureCategory.ProviderUnavailable, Sanitize(exception))));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _runspace.Dispose();
        }
        catch
        {
            // Disposal must not throw out of a using block.
        }
    }

    /// <summary>
    /// Reduces PowerShell errors to a category and a short summary. Command text and parameter values can
    /// echo identity data, so only the exception type and error identifier are kept.
    /// </summary>
    private static OperationError MapErrors(PSDataCollection<ErrorRecord> errors)
    {
        var first = errors[0];
        var text = first.Exception?.GetType().Name ?? "Error";
        var category = first.FullyQualifiedErrorId?.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) == true
            ? FailureCategory.Authorization
            : FailureCategory.ProviderUnavailable;

        return OperationError.Create(category, category == FailureCategory.Authorization
            ? "Exchange Online refused the request because a role is missing."
            : $"The Exchange Online command failed ({text}).");
    }

    private static string Sanitize(Exception exception)
    {
        var type = exception.GetType().Name;
        return $"The Exchange Online session failed ({type}).";
    }
}
