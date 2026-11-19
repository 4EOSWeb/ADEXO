using ADEXO.Contracts.Time;

namespace ADEXO.ExchangeOnline;

/// <summary>The connection states the UI must distinguish.</summary>
public enum ExchangeConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    AccessDenied,
    AuthenticationFailed,
    ModuleUnavailable,
    Failed,
}

/// <summary>
/// Owns the Exchange session lifecycle. Connection is an explicit administrator action, never automatic
/// on startup, and authentication is interactive modern authentication. Nothing is stored.
/// </summary>
public interface IExchangeConnectionService
{
    ExchangeConnectionState State { get; }

    /// <summary>True once a session exists and reads may be attempted.</summary>
    bool IsConnected { get; }

    /// <summary>The connected organization, for display. Never a credential.</summary>
    string? Organization { get; }

    Task<ExchangeConnectionState> ConnectAsync(string organization, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The production connection service, wrapping a runspace. It reports a module-loading failure distinctly
/// from an authentication failure, because the two need different administrator responses.
/// </summary>
public sealed class ExchangeConnectionService : IExchangeConnectionService, IDisposable
{
    private readonly ExchangeRunspace _runspace;
    private readonly IClock _clock;

    public ExchangeConnectionService(ExchangeRunspace runspace, IClock clock)
    {
        _runspace = runspace ?? throw new ArgumentNullException(nameof(runspace));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ExchangeConnectionState State { get; private set; } = ExchangeConnectionState.Disconnected;

    public bool IsConnected => State == ExchangeConnectionState.Connected && _runspace.IsConnected;

    public string? Organization { get; private set; }

    public async Task<ExchangeConnectionState> ConnectAsync(string organization, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);

        State = ExchangeConnectionState.Connecting;
        Organization = organization;

        var module = await _runspace.EnsureModuleAsync(cancellationToken).ConfigureAwait(false);
        if (!module.IsSuccess)
        {
            State = module.Error?.Category == Contracts.Results.FailureCategory.ProviderUnavailable
                ? ExchangeConnectionState.ModuleUnavailable
                : ExchangeConnectionState.Failed;
            return State;
        }

        var connected = await _runspace.ConnectInteractiveAsync(organization, cancellationToken).ConfigureAwait(false);
        if (connected.IsSuccess)
        {
            State = ExchangeConnectionState.Connected;
            return State;
        }

        if (connected.IsCanceled)
        {
            State = ExchangeConnectionState.Disconnected;
            return State;
        }

        State = connected.Error?.Category switch
        {
            Contracts.Results.FailureCategory.Authorization => ExchangeConnectionState.AccessDenied,
            Contracts.Results.FailureCategory.Authentication => ExchangeConnectionState.AuthenticationFailed,
            _ => ExchangeConnectionState.Failed,
        };

        return State;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _runspace.Disconnect();
        State = ExchangeConnectionState.Disconnected;
        Organization = null;
        return Task.CompletedTask;
    }

    public void Dispose() => _runspace.Dispose();
}
