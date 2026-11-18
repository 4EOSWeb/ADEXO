using ADEXO.Contracts.Capabilities;
using ADEXO.Contracts.Preview;
using ADEXO.Contracts.Validation;

namespace ADEXO.Core.Operations;

public static class ExecutionGuard
{
    public static bool CanEnterExecuting(ValidationResult validation, ProviderCapability capability, ChangePreview preview)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(preview);
        if (validation.HasBlockingErrors)
        {
            return false;
        }

        if (!capability.Available || !capability.Authenticated || !capability.Authorized || !capability.CanWrite)
        {
            return false;
        }

        if (preview.Items.Count == 0 || preview.Items.Any(item => !item.IsEditable))
        {
            return false;
        }

        return true;
    }
}
