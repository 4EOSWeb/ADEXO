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

        // Converters are declared in MainWindow.xaml's Window.Resources. They are deliberately not
        // created here as well: adding the same key twice throws during InitializeComponent.
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
