using ADEXO.Contracts.Operations;

namespace ADEXO.Contracts.Correlation;

public sealed class CorrelationContext
{
    public CorrelationContext(OperationId operationId) => OperationId = operationId;

    public OperationId OperationId { get; }

    public string? ActiveDirectoryOperation { get; private set; }

    public string? GraphRequestId { get; private set; }

    public string? ExchangeRequest { get; private set; }

    public string? PowerShellOperation { get; private set; }

    public void AttachActiveDirectory(string operation) => ActiveDirectoryOperation = Require(operation);

    public void AttachGraphRequest(string requestId) => GraphRequestId = Require(requestId);

    public void AttachExchangeRequest(string request) => ExchangeRequest = Require(request);

    public void AttachPowerShellOperation(string operation) => PowerShellOperation = Require(operation);

    private static string Require(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
