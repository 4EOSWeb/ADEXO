using System.Windows;
using ADEXO.ActiveDirectory;
using ADEXO.Contracts.Time;
using ADEXO.Core.Correlation;
using ADEXO.Core.Launch;
using ADEXO.Infrastructure.Cloud;
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

        // The cloud section is attached after the window exists so it can parent the broker dialog to
        // the real window handle. An absent or invalid configuration leaves the section reporting that
        // it is not configured, and the AD view runs unchanged.
        var (components, reason) = CloudSectionFactory.Create(CloudSectionFactory.ResolveConfigurationPath(), clock);
        if (components is not null)
        {
            model.AttachCloud(new CloudSectionViewModel(
                components.Authentication,
                components.Session,
                () => BuildSubject(model),
                () => new System.Windows.Interop.WindowInteropHelper(window).Handle));
        }
        else
        {
            // Surface why the section is unavailable. A flat "not configured" hides an invalid
            // identifier or an unreadable file, which makes a lab misconfiguration look like a
            // missing feature.
            model.AttachCloud(CloudSectionViewModel.NotConfigured(
                reason ?? "Microsoft 365 is not configured for this installation."));
        }

        window.Show();
        _ = model.StartAsync(e.Args);
    }

    /// <summary>
    /// Projects the verified AD user into the correlation subject. Only the verified identity and its
    /// corroborating attributes are carried; nothing here can create a cloud match on its own.
    /// </summary>
    private static AdCorrelationSubject? BuildSubject(MainViewModel model)
    {
        var user = model.User;
        return user is null
            ? null
            : new AdCorrelationSubject
            {
                ObjectGuid = user.ObjectGuid,
                SecurityIdentifier = user.ObjectSid,
                DistinguishedName = user.DistinguishedName,
                UserPrincipalName = user.UserPrincipalName,
                Mail = user.Mail,
                DisplayName = user.DisplayName,
            };
    }
}
