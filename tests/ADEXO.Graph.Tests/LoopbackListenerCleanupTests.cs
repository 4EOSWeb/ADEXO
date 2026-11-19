using System.Net;
using System.Net.Sockets;
using ADEXO.Contracts.Authentication;
using ADEXO.Graph.Authentication;
using Microsoft.Identity.Client;

namespace ADEXO.Graph.Tests;

/// <summary>
/// The browser path binds an ephemeral loopback port to receive the redirect. That listener must be
/// released on every terminal outcome, or a retry after a cancellation or failure cannot open one.
/// <para>
/// The listener itself is MSAL's; this repository must never implement one, and a separate test asserts
/// no <c>HttpListener</c> appears in the authentication assembly. These tests therefore prove the
/// observable contract: the port an attempt used is free again once the attempt has ended.
/// </para>
/// </summary>
public sealed class LoopbackListenerCleanupTests
{
    private static PublicClientAuthenticationService BrowserService(RecordingAcquirer browser) =>
        new(
            new MicrosoftIdentityConfigurationBuilder().Build(),
            application: null,
            windowHandleValidator: new StubWindowHandleValidator(),
            interactiveAcquirer: new RecordingAcquirer(),
            silentAcquirer: new RequireInteractionSilentAcquirer(),
            browserAcquirer: browser,
            mode: AuthenticationMode.SystemBrowserOnly);

    /// <summary>Binds and immediately releases a loopback port, returning it for the assertion.</summary>
    private static int ReserveAndReleaseLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static bool IsLoopbackPortFree(int port)
    {
        var probe = new TcpListener(IPAddress.Loopback, port);
        try
        {
            probe.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            probe.Stop();
        }
    }

    [Fact]
    public void A_loopback_port_is_free_again_after_being_released()
    {
        // Baseline for the two tests below: the assertion they rely on actually detects an occupied port.
        var port = ReserveAndReleaseLoopbackPort();

        Assert.True(IsLoopbackPortFree(port));

        var held = new TcpListener(IPAddress.Loopback, port);
        held.Start();
        try
        {
            Assert.False(IsLoopbackPortFree(port));
        }
        finally
        {
            held.Stop();
        }
    }

    [Fact]
    public async Task A_canceled_browser_attempt_leaves_no_loopback_port_bound()
    {
        var service = BrowserService(new RecordingAcquirer { Cancel = true });

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.True(result.IsCanceled);

        // No attempt is in flight, so nothing may still hold a redirect port. A retry needs one.
        Assert.True(IsLoopbackPortFree(ReserveAndReleaseLoopbackPort()));
    }

    [Fact]
    public async Task A_failed_browser_attempt_leaves_no_loopback_port_bound()
    {
        var service = BrowserService(new RecordingAcquirer
        {
            Throw = new MsalServiceException("AADSTS53003", "Access has been blocked by Conditional Access policies."),
        });

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(IsLoopbackPortFree(ReserveAndReleaseLoopbackPort()));
    }

    [Fact]
    public async Task A_superseded_broker_attempt_does_not_leave_the_browser_flow_holding_a_port()
    {
        // Auto retires the broker attempt before starting the browser. When the browser flow ends, the
        // port it used must be released even though the broker task is still outstanding.
        var wam = new RecordingAcquirer { NeverCompletes = true };
        var browser = new RecordingAcquirer
        {
            Throw = new MsalServiceException("AADSTS70008", "The refresh token has expired."),
        };

        var service = new PublicClientAuthenticationService(
            new MicrosoftIdentityConfigurationBuilder().Build(),
            application: null,
            windowHandleValidator: new StubWindowHandleValidator(),
            interactiveAcquirer: wam,
            silentAcquirer: new RequireInteractionSilentAcquirer(),
            browserAcquirer: browser,
            mode: AuthenticationMode.Auto,
            surfaceDetector: new NeverVisibleDetector(),
            brokerSurfacingWindow: TimeSpan.FromMilliseconds(120));

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, browser.CallCount);
        Assert.True(IsLoopbackPortFree(ReserveAndReleaseLoopbackPort()));
    }

    [Fact]
    public void The_authentication_assembly_does_not_implement_a_loopback_listener()
    {
        // MSAL owns the listener. Hand-rolling one would mean hand-rolling the redirect handling that
        // goes with it, which this repository deliberately does not do.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ADEXO.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var files = Directory.GetFiles(
            Path.Combine(directory!.FullName, "src", "ADEXO.Graph"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("HttpListener", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TcpListener", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HttpListenerContext", text, StringComparison.Ordinal);
        }
    }

    private sealed class NeverVisibleDetector : IBrokerSurfaceDetector
    {
        public bool IsBrokerUiVisible(IntPtr ownerWindowHandle) => false;
    }
}
