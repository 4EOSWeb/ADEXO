using System.IO;
using System.Xml.Linq;

namespace ADEXO.Desktop.Tests;

/// <summary>
/// Guards the wiring between the view models and the window.
/// <para>
/// A view-model property that nothing binds is invisible, and a live run found exactly that: the cloud
/// session's status message carried the sign-in progress and the fallback notice, but no element bound
/// to it, so an operator saw a browser open with no explanation. Source-level assertions are used
/// because these are declared in XAML and cannot be reached through the view models alone.
/// </para>
/// </summary>
public sealed class MainWindowBindingTests
{
    private static XDocument LoadWindow()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ADEXO.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, "src", "ADEXO.Desktop", "MainWindow.xaml");
        Assert.True(File.Exists(path), $"The window markup was not found at {path}.");
        return XDocument.Load(path);
    }

    private static string[] Bindings(XDocument document) =>
        [.. document.Descendants()
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value is not null)
            .SelectMany(value => System.Text.RegularExpressions.Regex.Matches(value!, @"\{Binding\s+([^,}\s]+)")
                .Select(match => match.Groups[1].Value))];

    [Fact]
    public void The_cloud_status_message_is_bound_so_progress_and_fallback_reach_the_screen()
    {
        // "Signing in with Windows..." and "Windows sign-in couldn't open. Continuing securely in your
        // browser..." live on this property. If it is unbound, the required UX never appears.
        Assert.Contains("Cloud.Message", Bindings(LoadWindow()));
    }

    [Fact]
    public void The_product_status_line_and_section_message_are_bound()
    {
        var bindings = Bindings(LoadWindow());

        Assert.Contains("Product.StatusLine", bindings);
        Assert.Contains("Product.SectionMessage", bindings);
    }

    [Fact]
    public void Every_cloud_command_is_reachable_from_the_window()
    {
        // A command with no binding is dead: the cancel path in particular existed only in the view
        // model, so the operator had no way to abandon a hung sign-in.
        var document = LoadWindow();
        var commandBindings = document.Descendants()
            .Select(element => element.Attribute("Command")?.Value)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

        foreach (var name in new[] { "SignInCommand", "CancelCommand", "SignOutCommand", "RefreshCommand" })
        {
            Assert.Contains(commandBindings, binding => binding.Contains($"Cloud.{name}", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Sign_in_names_the_normal_ui_wording()
    {
        var document = LoadWindow();
        var labels = document.Descendants()
            .Select(element => element.Attribute("Content")?.Value)
            .Where(value => value is not null)
            .ToArray();

        Assert.Contains("Sign in to Microsoft 365", labels);
    }
}
