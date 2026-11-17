using System.Windows;
using ADEXO.ActiveDirectory;
using ADEXO.Contracts.Time;
using ADEXO.Core.Launch;
using ADEXO.Infrastructure.Time;

namespace ADEXO.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        IClock clock = new SystemClock();
        var model = new MainViewModel(
            new AducLaunchRequestParser(clock),
            new ActiveDirectoryProvider(new LdapDirectoryReader(TimeSpan.FromSeconds(15))),
            clock);
        var window = new MainWindow(model);
        window.Show();
        _ = model.StartAsync(e.Args);
    }
}
