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

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
