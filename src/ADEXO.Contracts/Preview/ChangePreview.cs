using ADEXO.Contracts.Authority;
using ADEXO.Contracts.Capabilities;

namespace ADEXO.Contracts.Preview;

public sealed record ChangePreviewItem(
    ProviderKind Provider,
    string Property,
    string? CurrentValue,
    string? RequestedValue,
    SourceOfAuthority Source,
    string Impact,
    bool RequiresConfirmation,
    bool IsDestructive,
    bool IsSecuritySensitive,
    bool IsEditable);

public sealed class ChangePreview
{
    public ChangePreview(IReadOnlyList<ChangePreviewItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = items;
    }

    public IReadOnlyList<ChangePreviewItem> Items { get; }

    public bool RequiresConfirmation =>
        Items.Any(item => item.RequiresConfirmation || item.IsDestructive || item.IsSecuritySensitive);
}
