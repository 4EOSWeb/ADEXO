using System.Windows;
using System.Windows.Controls;
using ADEXO.Desktop;

namespace ADEXO.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    public MainWindow(MainViewModel model)
    {
        _model = model;

        // Converter resources referenced by the templates are created here so the XAML stays declarative.
        Resources["SeverityBrush"] = new SeverityBrushConverter();
        Resources["BoolToVisible"] = new BoolToVisibleConverter();
        Resources["EmptyToCollapsed"] = new EmptyToCollapsedConverter();

        InitializeComponent();
        DataContext = model;
    }

    private void CopyDefault(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(_model.BuildDiagnostics(includeSensitive: false));

    /// <summary>Passes the window handle so the broker dialog is owned by ADEXO.</summary>
    private void SignInCloud(object sender, RoutedEventArgs e) =>
        _ = _model.Cloud.SignInAsync(new System.Windows.Interop.WindowInteropHelper(this).Handle);

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
