using ADEXO.Contracts.Capabilities;
using ADEXO.Contracts.Features;
using ADEXO.Contracts.Operations;
using ADEXO.Contracts.Preview;
using ADEXO.Contracts.Results;
using ADEXO.Contracts.Validation;

namespace ADEXO.Contracts.Operations;

public interface IChangeOperation
{
    OperationId Id { get; }

    OperationState State { get; }

    Task<ProviderResult<ChangePreview>> ReadAsync(CancellationToken cancellationToken);

    Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken);

    Task<ChangePreview> PreviewAsync(CancellationToken cancellationToken);

    Task<ProviderCapability> AuthorizeAsync(KnownFeatureId feature, CancellationToken cancellationToken);

    Task<OperationResult<VerificationResult>> ExecuteAsync(CancellationToken cancellationToken);

    Task<VerificationResult> VerifyAsync(CancellationToken cancellationToken);
}
