using System.Collections.ObjectModel;
using System.Management.Automation;
using ADEXO.Contracts.Results;

namespace ADEXO.ExchangeOnline;

/// <summary>
/// The seam the mailbox provider composes against. Implemented by the runspace adapter in production and
/// by a recorder in tests, so the provider can be exercised without a tenant.
/// </summary>
public interface IExchangeInvoker
{
    Task<ProviderResult<Collection<PSObject>>> InvokeAsync(
        ExchangeInvocation invocation,
        CancellationToken cancellationToken);
}

/// <summary>Adapts <see cref="ExchangeRunspace"/> to the invoker seam.</summary>
public sealed class ExchangeRunspaceInvoker : IExchangeInvoker
{
    private readonly ExchangeRunspace _runspace;

    public ExchangeRunspaceInvoker(ExchangeRunspace runspace) =>
        _runspace = runspace ?? throw new ArgumentNullException(nameof(runspace));

    public Task<ProviderResult<Collection<PSObject>>> InvokeAsync(
        ExchangeInvocation invocation,
        CancellationToken cancellationToken) =>
        _runspace.InvokeAsync(invocation, cancellationToken);
}
