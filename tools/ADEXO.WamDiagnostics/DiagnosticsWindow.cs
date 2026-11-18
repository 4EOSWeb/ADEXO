using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

namespace ADEXO.WamDiagnostics;

/// <summary>
/// A deliberately minimal WPF harness for isolating a WAM failure from an ADEXO orchestration failure.
/// <para>
/// It does exactly what the product does around the interactive call and nothing else: one real
/// window, a handle taken after the window is shown, the same public client, the interactive request
/// started on the UI thread. It never calls Microsoft Graph, never requests more than User.Read, and
/// never records a token.
/// </para>
/// <para>
/// This exists so a hang can be attributed. If this harness shows the account picker, the environment
/// and registration are fine and the defect is in ADEXO. If it hangs the same way with a valid UI
/// thread and handle, the problem is the environment or the broker.
/// </para>
/// </summary>
public sealed partial class DiagnosticsWindow : Window
{
    private readonly TextBlock _log = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new("Consolas") };
    private readonly Button _signIn = new() { Content = "Sign in (User.Read only)", Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _signOut = new() { Content = "Remove cached account", Margin = new Thickness(0, 8, 0, 0) };
    private readonly Stopwatch _elapsed = new();
    private IPublicClientApplication? _application;
    private int _checkpointCount;

    private static readonly string[] Scopes = ["User.Read"];

    public DiagnosticsWindow()
    {
        Title = "ADEXO WAM diagnostics";
        Width = 760;
        Height = 620;

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = "Isolates WAM from ADEXO orchestration. Requests User.Read only. No Graph calls. No token display.",
            TextWrapping = TextWrapping.Wrap,
        });        panel.Children.Add(_signIn);
        panel.Children.Add(_signOut);
        panel.Children.Add(new ScrollViewer { Height = 460, Margin = new Thickness(0, 12, 0, 0), Content = _log });

        Content = panel;

        _signIn.Click += async (_, _) => await SignInAsync();
        _signOut.Click += async (_, _) => await SignOutAsync();

        Loaded += (_, _) => LogEnvironment();
    }

    private void LogEnvironment()
    {
        var handle = new WindowInteropHelper(this).Handle;
        Log($"window shown: hwnd=0x{handle.ToInt64():X} valid={handle != IntPtr.Zero}");
        Log($"ui thread: managed={Environment.CurrentManagedThreadId} apartment={Thread.CurrentThread.GetApartmentState()}");
        Log($"dispatcher check access: {Dispatcher.CheckAccess()}");
        Log($"process id: {Environment.ProcessId}");
        Log($"os: {Environment.OSVersion.VersionString}");
        Log("ready. press sign in. the picker should appear within a few seconds.");
    }

    private async Task SignInAsync()
    {
        _signIn.IsEnabled = false;
        _elapsed.Restart();
        _checkpointCount = 0;
        _log.Text = string.Empty;

        try
        {
            var configuration = LoadConfiguration();
            if (configuration is null)
            {
                Log("no configuration file. place adexo.wamdiagnostics.json beside this executable with tenantId and clientId.");
                return;
            }

            _application ??= PublicClientApplicationBuilder
                .Create(configuration.Value.ClientId)
                .WithAuthority($"https://login.microsoftonline.com/{configuration.Value.TenantId:D}", validateAuthority: true)
                .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows))
                .WithClientName("ADEXO WAM diagnostics")
                .WithLogging(OnMsalLog, LogLevel.Info, enablePiiLogging: false, enableDefaultPlatformLogging: true)
                .Build();

            AuthenticationResult? silent = null;
            Checkpoint("before-getaccounts");
            var accounts = (await _application.GetAccountsAsync().ConfigureAwait(true)).ToList();
            Checkpoint($"after-getaccounts count={accounts.Count}");

            var account = accounts.FirstOrDefault();
            if (account is null)
            {
                Checkpoint("before-operatingsystemaccount");
                // The operating-system account only resolves on a device with an Entra PRT. On a purely
                // on-premises domain-joined machine this token source cannot resolve, and asking for it
                // hangs. Skip it when the device has no PRT.
                var hasPrt = DeviceHasEntraPrt();
                Checkpoint($"device-has-prt={hasPrt}");
                if (hasPrt)
                {
                    account = PublicClientApplication.OperatingSystemAccount;
                }

                Checkpoint($"after-operatingsystemaccount present={account is not null}");
            }

            if (account is not null)
            {
                Checkpoint("before-silent-acquire");
                try
                {
                    silent = await _application.AcquireTokenSilent(Scopes, account).ExecuteAsync().ConfigureAwait(true);
                    Checkpoint("after-silent-acquire");
                }
                catch (MsalUiRequiredException)
                {
                    Checkpoint("silent-reported-ui-required");
                    Log("silent acquisition reported interaction is required, which is expected.");
                }
            }

            if (silent is not null)
            {
                Report(silent, "silent");
                return;
            }

            // The whole point of this harness: the interactive call starts here, on the UI thread,
            // with a handle taken from the live window.
            var handle = new WindowInteropHelper(this).Handle;
            Log($"interactive: hwnd=0x{handle.ToInt64():X} uiThread={Environment.CurrentManagedThreadId} "
                + $"dispatcherCheckAccess={Dispatcher.CheckAccess()}");
            Checkpoint("interactive-initiate");

            var result = await _application
                .AcquireTokenInteractive(Scopes)
                .WithParentActivityOrWindow(handle)
                .ExecuteAsync()
                .ConfigureAwait(true);

            Checkpoint("interactive-returned");
            Report(result, "interactive");
        }
        catch (MsalClientException exception)
        {
            Log($"MSAL client error: code={exception.ErrorCode}");
            Log($"message: {exception.Message}");
        }
        catch (MsalServiceException exception)
        {
            Log($"MSAL service error: code={exception.ErrorCode} status={exception.StatusCode}");
            Log($"message: {exception.Message}");
        }
        catch (OperationCanceledException)
        {
            Log("canceled.");
        }
        catch (Exception exception)
        {
            Log($"unexpected: type={exception.GetType().FullName}");
            Log($"message: {exception.Message}");
        }
        finally
        {
            _elapsed.Stop();
            Log($"elapsed: {_elapsed.Elapsed.TotalSeconds:F1}s");
            _signIn.IsEnabled = true;
        }
    }

    /// <summary>Reports metadata only. The token is never written anywhere.</summary>
    private void Report(AuthenticationResult result, string flow)
    {
        Log($"{flow} SUCCESS");
        Log($"  account present: {result.Account is not null}");
        Log($"  tenant id: {result.TenantId}");
        Log($"  token acquired: yes (value not recorded)");
        Log($"  scope count: {result.Scopes?.Count() ?? 0}");
    }

    private async Task SignOutAsync()
    {
        if (_application is null)
        {
            Log("nothing to remove.");
            return;
        }

        var accounts = await _application.GetAccountsAsync().ConfigureAwait(true);
        foreach (var account in accounts)
        {
            await _application.RemoveAsync(account).ConfigureAwait(true);
        }

        Log($"removed {accounts.Count()} cached account(s) for this application only. "
            + "This does not remove the account from Windows.");
    }

    private void Checkpoint(string stage)
    {
        _checkpointCount++;
        Log($"checkpoint {_checkpointCount}: {stage} thread={Environment.CurrentManagedThreadId} "
            + $"apartment={Thread.CurrentThread.GetApartmentState()} elapsed={_elapsed.Elapsed.TotalSeconds:F2}s");
    }

    /// <summary>
    /// True when the device holds an Entra primary refresh token, which is what makes the operating
    /// system account usable. A purely on-premises domain-joined machine returns false, and asking for
    /// the OS account there cannot resolve.
    /// </summary>
    private static bool DeviceHasEntraPrt()
    {
        try
        {
            var info = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dsregcmd.exe",
                Arguments = "/status",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (info is null)
            {
                return false;
            }

            var output = info.StandardOutput.ReadToEnd();
            info.WaitForExit(5000);

            return output.Contains("AzureAdPrt : YES", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the two identifiers from a file beside the executable. Only identifiers are accepted; a
    /// secret-looking property aborts rather than being ignored.
    /// </summary>
    private static (Guid TenantId, string ClientId)? LoadConfiguration()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "adexo.wamdiagnostics.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var text = File.ReadAllText(path);
        if (text.Contains("password", StringComparison.OrdinalIgnoreCase)
            || text.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || text.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The diagnostics configuration must contain identifiers only.");
        }

        using var document = System.Text.Json.JsonDocument.Parse(text);
        var tenant = document.RootElement.GetProperty("tenantId").GetString();
        var client = document.RootElement.GetProperty("clientId").GetString();

        if (!Guid.TryParse(tenant, out var tenantId) || string.IsNullOrWhiteSpace(client))
        {
            throw new InvalidOperationException("The diagnostics configuration needs a valid tenantId and clientId.");
        }

        return (tenantId, client);
    }

    private void Log(string message) => _log.Text += message + Environment.NewLine;

    /// <summary>
    /// Records MSAL's own diagnostics with personal information disabled.
    /// <para>
    /// MSAL invokes this from whichever thread is doing the work, which is usually not the UI thread.
    /// Writing to the log block directly from there throws a thread-affinity error that propagates out
    /// through the MSAL await and looks exactly like an authentication failure. The write is therefore
    /// marshalled to the dispatcher.
    /// </para>
    /// </summary>
    private void OnMsalLog(LogLevel level, string message, bool containsPii)
    {
        if (containsPii)
        {
            return;
        }

        // A defensive filter: even with PII logging off, never write something that looks like a token.
        if (message.Contains("eyJ", StringComparison.Ordinal))
        {
            return;
        }

        var line = message.Length > 240 ? message[..240] + "..." : message;
        AppendOnUiThread($"  [msal {level}] {line}");
    }

    /// <summary>Appends to the log from the UI thread regardless of the calling thread.</summary>
    private void AppendOnUiThread(string message)
    {
        if (Dispatcher.CheckAccess())
        {
            _log.Text += message + Environment.NewLine;
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => _log.Text += message + Environment.NewLine));
    }

    [STAThread]
    public static void Main()
    {
        var application = new Application();
        application.Run(new DiagnosticsWindow());
    }
}
