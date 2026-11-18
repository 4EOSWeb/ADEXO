using System.Windows;

namespace ADEXO.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    public MainWindow(MainViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
    }

    private void CopyDefault(object sender, RoutedEventArgs e) => Clipboard.SetText(_model.BuildDiagnostics(includeSensitive: false));

    private void CopyDetailed(object sender, RoutedEventArgs e) => Clipboard.SetText(_model.BuildDiagnostics(includeSensitive: true));

    /// <summary>
    /// Passes the window handle so the broker sign-in dialog is owned by ADEXO. Without it the account
    /// picker can appear detached from the application.
    /// </summary>
    private void SignInCloud(object sender, RoutedEventArgs e) =>
        _ = _model.Cloud.SignInAsync(new System.Windows.Interop.WindowInteropHelper(this).Handle);

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
