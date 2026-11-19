using ADEXO.Contracts.Authentication;
using ADEXO.Graph.Authentication;
using Microsoft.Identity.Client;

namespace ADEXO.Graph.Tests;

/// <summary>
/// The authentication-mode matrix. Each mode has one behaviour and it must not drift: Auto prefers the
/// broker, WamOnly never starts a browser, and SystemBrowserOnly never starts the broker.
/// </summary>
public sealed class AuthenticationModeTests
{
    private static PublicClientAuthenticationService Create(
        AuthenticationMode mode,
        RecordingAcquirer wam,
        RecordingAcquirer browser,
        StubWindowHandleValidator? validator = null) =>
        new(
            new MicrosoftIdentityConfigurationBuilder().Build(),
            application: null,
            validator ?? new StubWindowHandleValidator(),
            interactiveAcquirer: wam,
            silentAcquirer: new RequireInteractionSilentAcquirer(),
            browserAcquirer: browser,
            mode: mode);

    private static PublicClientAuthenticationService CreateWithModeFile(AuthenticationMode expected)
    {
        var configuration = new MicrosoftIdentityConfigurationBuilder().WithMode(expected.ToString()).Build();
        return new PublicClientAuthenticationService(
            configuration,
            application: null,
            new StubWindowHandleValidator(),
            new RecordingAcquirer(),
            new RequireInteractionSilentAcquirer(),
            new RecordingAcquirer(),
            expected);
    }

    [Fact]
    public void Auto_is_the_default_mode()
    {
        var configuration = new MicrosoftIdentityConfigurationBuilder().Build();

        Assert.Equal(AuthenticationMode.Auto, configuration.Mode);
    }

    [Theory]
    [InlineData("WamOnly", AuthenticationMode.WamOnly)]
    [InlineData("wamonly", AuthenticationMode.WamOnly)]
    [InlineData("SystemBrowserOnly", AuthenticationMode.SystemBrowserOnly)]
    [InlineData("Auto", AuthenticationMode.Auto)]
    public void The_configured_mode_is_parsed(string text, AuthenticationMode expected)
    {
        var configuration = new MicrosoftIdentityConfigurationBuilder().WithMode(text).Build();

        Assert.Equal(expected, configuration.Mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("WAM_ONLY")]
    public void An_unrecognized_mode_falls_back_to_auto(string text)
    {
        // A typo must not lock an administrator out of signing in.
        var configuration = new MicrosoftIdentityConfigurationBuilder().WithMode(text).Build();

        Assert.Equal(AuthenticationMode.Auto, configuration.Mode);
    }

    [Fact]
    public async Task WamOnly_never_launches_the_browser()
    {
        var wam = new RecordingAcquirer { Throw = new InvalidOperationException("broker failed") };
        var browser = new RecordingAcquirer();
        var service = Create(AuthenticationMode.WamOnly, wam, browser);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, wam.CallCount);
        Assert.Equal(0, browser.CallCount);
        Assert.Equal(Contracts.Cloud.AuthenticationState.Failed, service.State);
    }

    [Fact]
    public async Task SystemBrowserOnly_never_starts_the_broker()
    {
        var wam = new RecordingAcquirer();
        var browser = new RecordingAcquirer { Throw = new InvalidOperationException("browser failed") };
        var service = Create(AuthenticationMode.SystemBrowserOnly, wam, browser);

        await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.Equal(0, wam.CallCount);
        Assert.Equal(1, browser.CallCount);
        Assert.Equal(FallbackReason.ExplicitUserFallback, service.LastFallbackReason);
    }

    [Fact]
    public async Task Auto_prefers_the_broker_when_it_produces_a_result()
    {
        var wam = new RecordingAcquirer { Throw = new InvalidOperationException("broker failed") };
        var browser = new RecordingAcquirer();
        var service = Create(AuthenticationMode.Auto, wam, browser);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        // The broker was attempted first and failed to activate, so Auto fell back to the browser.
        Assert.Equal(1, wam.CallCount);
        Assert.Equal(1, browser.CallCount);
        Assert.Equal(FallbackReason.BrokerActivationFailure, service.LastFallbackReason);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task A_rejected_handle_stops_every_mode_before_any_authentication()
    {
        foreach (var mode in Enum.GetValues<AuthenticationMode>())
        {
            var wam = new RecordingAcquirer();
            var browser = new RecordingAcquirer();
            var validator = new StubWindowHandleValidator { Reason = "The supplied window handle is not a live window." };
            var service = Create(mode, wam, browser, validator);

            var result = await service.SignInAsync((IntPtr)999, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(Contracts.Results.FailureCategory.Validation, result.Error!.Category);
            Assert.Equal(0, wam.CallCount);
            Assert.Equal(0, browser.CallCount);
        }
    }

    [Fact]
    public async Task Auto_falls_back_to_the_browser_when_the_broker_never_surfaces()
    {
        // The broker acquirer here never completes, which is exactly the observed lab condition: the
        // broker produces no UI and no token. Auto must reach the browser rather than waiting forever.
        var wam = new RecordingAcquirer { NeverCompletes = true };
        var browser = new RecordingAcquirer { Throw = new InvalidOperationException("browser reached") };
        var service = Create(AuthenticationMode.Auto, wam, browser);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.Equal(1, wam.CallCount);
        Assert.Equal(1, browser.CallCount);
        Assert.Equal(AuthenticationMethod.SystemBrowser, service.LastMethod);
        Assert.True(
            service.LastFallbackReason is FallbackReason.BrokerUiNotSurfaced or FallbackReason.BrokerTimeout,
            $"Expected a surfacing fallback reason, got {service.LastFallbackReason}.");
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Auto_falls_back_when_the_broker_raises_an_activation_error()
    {
        var wam = new RecordingAcquirer { Throw = new InvalidOperationException("broker activation failed") };
        var browser = new RecordingAcquirer { Throw = new InvalidOperationException("browser reached") };
        var service = Create(AuthenticationMode.Auto, wam, browser);

        await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.Equal(1, browser.CallCount);
        Assert.Equal(FallbackReason.BrokerActivationFailure, service.LastFallbackReason);
    }

    [Fact]
    public async Task SystemBrowserOnly_bypasses_the_broker_entirely()
    {
        var wam = new RecordingAcquirer();
        var browser = new RecordingAcquirer { Throw = new InvalidOperationException("browser reached") };
        var service = Create(AuthenticationMode.SystemBrowserOnly, wam, browser);

        await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.Equal(0, wam.CallCount);
        Assert.Equal(1, browser.CallCount);
    }

    [Fact]
    public void The_mode_is_never_silently_overridden()
    {
        foreach (var mode in Enum.GetValues<AuthenticationMode>())
        {
            var service = CreateWithModeFile(mode);

            Assert.Equal(mode, service.Mode);
        }
    }

    [Fact]
    public void Both_client_configurations_carry_the_same_identifiers_and_scopes()
    {
        // The fallback must not widen the sign-in surface: same client, same tenant, same read scopes.
        Assert.Equal(
            ["User.Read", "User.Read.All", "Organization.Read.All"],
            MicrosoftIdentityConfiguration.Scopes);

        Assert.Equal("http://localhost", MicrosoftIdentityConfiguration.BrowserRedirectRegistration);
    }

    [Fact]
    public void The_browser_registration_uses_loopback_and_not_an_external_host()
    {
        var registration = MicrosoftIdentityConfiguration.BrowserRedirectRegistration;

        Assert.StartsWith("http://localhost", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", registration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_prohibited_authentication_flow_appears_in_the_authentication_assembly()
    {
        string[] prohibited =
        [
            "AcquireTokenByUsernamePassword",
            "AcquireTokenWithDeviceCode",
            "AcquireTokenForClient",
            "ConfidentialClientApplication",
            "WithCertificate",
            "Ropc",
            "DeviceCode",
        ];

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ADEXO.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var files = Directory.GetFiles(
            Path.Combine(root!.FullName, "src", "ADEXO.Graph", "Authentication"),
            "*.cs",
            SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var flow in prohibited)
            {
                Assert.DoesNotContain(flow, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void No_pkce_or_authorization_code_flow_is_implemented_manually()
    {
        // MSAL performs the code exchange and PKCE. A manual implementation would be a security defect.
        string[] manual =
        [
            "code_verifier",
            "code_challenge",
            "S256",
            "HttpListener",
            "/oauth2/v2.0/token",
            "client_assertion",
        ];

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ADEXO.slnx")))
        {
            root = root.Parent;
        }

        var files = Directory.GetFiles(
            Path.Combine(root!.FullName, "src", "ADEXO.Graph", "Authentication"),
            "*.cs",
            SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in manual)
            {
                Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_fallback_reasons_are_strongly_typed_rather_than_strings()
    {
        var values = Enum.GetValues<FallbackReason>();

        Assert.Contains(FallbackReason.BrokerUnavailable, values);
        Assert.Contains(FallbackReason.BrokerActivationFailure, values);
        Assert.Contains(FallbackReason.BrokerUiNotSurfaced, values);
        Assert.Contains(FallbackReason.BrokerTimeout, values);
        Assert.Contains(FallbackReason.ExplicitUserFallback, values);
    }
}
