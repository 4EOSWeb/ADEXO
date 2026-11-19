using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Mailbox;

namespace ADEXO.Desktop;

/// <summary>
/// A display row: a label, a value, and an optional severity so the UI can colour semantic state without
/// the view model knowing anything about brushes.
/// </summary>
public sealed class DetailRow
{
    public required string Label { get; init; }

    public required string Value { get; init; }

    public DetailSeverity Severity { get; init; } = DetailSeverity.Normal;
}

/// <summary>Semantic severity, mapped to a brush by the view.</summary>
public enum DetailSeverity
{
    Normal,
    Muted,
    Good,
    Warning,
    Critical,
}

/// <summary>A titled group of rows, one per UI section.</summary>
public sealed class DetailGroup
{
    public required string Title { get; init; }

    public ObservableCollection<DetailRow> Rows { get; init; } = [];

    /// <summary>A short clarifying note shown beneath the rows, such as a forwarding warning.</summary>
    public string? Footnote { get; set; }
}

/// <summary>
/// One delegated-tab view model: a title, its groups, an empty-state explanation, and a busy flag.
/// Every tab is independently populated, so one provider failing leaves the others readable.
/// </summary>
public sealed class DetailSectionViewModel : INotifyPropertyChanged
{
    private string _emptyMessage = "Load a user from Active Directory Users and Computers to begin.";
    private bool _isBusy;
    private bool _isLoaded;

    public DetailSectionViewModel(string title) => Title = title;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public ObservableCollection<DetailGroup> Groups { get; } = [];

    public string EmptyMessage
    {
        get => _emptyMessage;
        private set => Set(ref _emptyMessage, value);
    }

    /// <summary>True while a provider read for this section is in flight.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    /// <summary>True once this section has content worth showing.</summary>
    public bool HasContent => _isLoaded && Groups.Count > 0;

    public bool ShowEmptyState => !HasContent && !IsBusy;

    public void BeginLoad(string message = "Reading…")
    {
        EmptyMessage = message;
        IsBusy = true;
        RaiseDerived();
    }

    public void EndLoad()
    {
        IsBusy = false;
        RaiseDerived();
    }

    public void SetEmpty(string message)
    {
        Groups.Clear();
        _isLoaded = false;
        EmptyMessage = message;
        RaiseDerived();
    }

    public void Populate(params DetailGroup[] groups)
    {
        Groups.Clear();
        foreach (var group in groups)
        {
            Groups.Add(group);
        }

        _isLoaded = groups.Length > 0;
        RaiseDerived();
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Builds the display rows from provider models, keeping formatting in one place.</summary>
public static class DetailProjection
{
    public static DetailRow Row(string label, string? value, DetailSeverity severity = DetailSeverity.Normal) =>
        new() { Label = label, Value = string.IsNullOrWhiteSpace(value) ? "—" : value, Severity = severity };

    public static DetailRow Boolean(string label, bool? value, string trueText = "Yes", string falseText = "No") =>
        value is null
            ? Row(label, "Unavailable", DetailSeverity.Muted)
            : Row(label, value.Value ? trueText : falseText, value.Value ? DetailSeverity.Normal : DetailSeverity.Muted);

    public static string Date(DateTimeOffset? value) =>
        value is null ? "—" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
