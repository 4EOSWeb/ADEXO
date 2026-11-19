using System.Collections.ObjectModel;
using System.Management.Automation;
using ADEXO.Contracts.Results;
using ADEXO.Contracts.Time;

namespace ADEXO.ExchangeOnline.Tests;

/// <summary>
/// Records every typed invocation and returns scripted results. It exists so the provider can be
/// exercised without a tenant, and so a test can assert exactly which commands and parameters were used.
/// </summary>
internal sealed class RecordingExchangeInvoker : IExchangeInvoker
{
    private readonly Dictionary<string, Queue<Func<Collection<PSObject>>>> _responses =
        new(StringComparer.OrdinalIgnoreCase);

    public List<ExchangeInvocation> Invocations { get; } = [];

    public OperationError? Error { get; set; }

    public bool Cancel { get; set; }

    public RecordingExchangeInvoker Returns(string command, params PSObject[] rows)
    {
        if (!_responses.TryGetValue(command, out var queue))
        {
            queue = new Queue<Func<Collection<PSObject>>>();
            _responses[command] = queue;
        }

        queue.Enqueue(() => new Collection<PSObject>(rows.ToList()));
        return this;
    }

    public RecordingExchangeInvoker ReturnsOnce(string command, params PSObject[] rows) => Returns(command, rows);

    public Task<ProviderResult<Collection<PSObject>>> InvokeAsync(
        ExchangeInvocation invocation,
        CancellationToken cancellationToken)
    {
        Invocations.Add(invocation);

        if (Cancel)
        {
            return Task.FromResult(ProviderResult<Collection<PSObject>>.Canceled());
        }

        if (Error is not null)
        {
            return Task.FromResult(ProviderResult<Collection<PSObject>>.Failed(Error));
        }

        if (_responses.TryGetValue(invocation.Command, out var queue) && queue.Count > 0)
        {
            return Task.FromResult(ProviderResult<Collection<PSObject>>.Success(queue.Dequeue()()));
        }

        return Task.FromResult(ProviderResult<Collection<PSObject>>.Success(new Collection<PSObject>()));
    }

    /// <summary>Finds the one invocation of a command, failing if it was called a different number of times.</summary>
    public ExchangeInvocation Single(string command)
    {
        var matches = Invocations.Where(i => i.Command.Equals(command, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(matches);
        return matches[0];
    }
}

internal sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow { get; } = new(2026, 11, 19, 13, 0, 0, TimeSpan.Zero);
}
