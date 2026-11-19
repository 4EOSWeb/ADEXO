using ADEXO.Contracts.Authentication;
using ADEXO.Contracts.Cloud;
using ADEXO.Graph.Authentication;
using Microsoft.Identity.Client;

namespace ADEXO.Graph.Tests;

/// <summary>
/// The approved fallback contract, asserted end to end at the service boundary.
/// <para>
/// The twenty-second window detects a broker that never shows usable interactive UI. It is not an
/// authentication deadline: once UI is observed, or broker interaction is demonstrably active, the
/// operator may take as long as account selection, credentials, MFA, Conditional Access, or a password
/// reset require. These tests pin both halves of that distinction, plus the attempt-generation rule that
/// stops a late broker result replacing a newer browser result.
/// </para>
/// </summary>
public sealed class AuthenticationFallbackTests
{
    private static readonly Guid ApprovedTenant = Guid.Parse("c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af");
    private static readonly Guid OtherTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static PublicClientAuthenticationService Create(
        AuthenticationMode mode,
        RecordingAcquirer wam,
        RecordingAcquirer browser,
        IBrokerSurfaceDetector? detector = null,
        TimeSpan? window = null,
        ISilentTokenAcquirer? silent = null) =>
        new(
            new MicrosoftIdentityConfigurationBuilder().Build(),
            application: null,
            new StubWindowHandleValidator(),
            interactiveAcquirer: wam,
            silentAcquirer: silent ?? new RequireInteractionSilentAcquirer(),
            browserAcquirer: browser,
            mode: mode,
            surfaceDetector: detector,
            brokerSurfacingWindow: window);

    [Fact]
    public async Task WAM_success_never_starts_the_browser()
    {
        var wam = new RecordingAcquirer { Result = AuthenticationResults.For(ApprovedTenant) };
        var browser = new RecordingAcquirer { Throw = new InvalidOperationException("the browser must not start") };

        var result = await Create(AuthenticationMode.Auto, wam, browser).SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, browser.CallCount);
    }

    [Fact]
    public async Task WAM_success_records_the_broker_as_the_actual_method_with_no_fallback()
    {
        var wam = new RecordingAcquirer { Result = AuthenticationResults.For(ApprovedTenant) };
        var service = Create(AuthenticationMode.Auto, wam, new RecordingAcquirer());

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthenticationMethod.WindowsBroker, service.LastMethod);
        Assert.Equal(FallbackReason.None, service.LastFallbackReason);
        Assert.True(result.Value!.BrokerUsed);
    }

    [Fact]
    public async Task Broker_ui_that_surfaces_and_takes_longer_than_the_window_does_not_fall_back()
    {
        // The operator is slow on purpose: this covers account selection, credentials, MFA, Conditional
        // Access, and a password reset, all of which may exceed the observation window.
        var wam = new RecordingAcquirer
        {
            Result = AuthenticationResults.For(ApprovedTenant),
            Delay = TimeSpan.FromMilliseconds(600),
        };
        var browser = new RecordingAcquirer { Throw = new InvalidOperationException("the browser must not start") };
        var detector = new TimedSurfaceDetector(TimeSpan.FromMilliseconds(50));

        var service = Create(
            AuthenticationMode.Auto,
            wam,
            browser,
            detector,
            window: TimeSpan.FromMilliseconds(150));

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, browser.CallCount);
        Assert.Equal(AuthenticationMethod.WindowsBroker, service.LastMethod);
        Assert.Equal(FallbackReason.None, service.LastFallbackReason);
    }

    [Fact]
    public async Task Broker_ui_that_never_surfaces_starts_the_browser()
    {
        var wam = new RecordingAcquirer { NeverCompletes = true };
        var browser = new RecordingAcquirer { Result = AuthenticationResults.For(ApprovedTenant) };
        var detector = new TimedSurfaceDetector(TimeSpan.MaxValue);

        var service = Create(
            AuthenticationMode.Auto,
            wam,
            browser,
            detector,
            window: TimeSpan.FromMilliseconds(150));

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, browser.CallCount);
        Assert.Equal(AuthenticationMethod.SystemBrowser, service.LastMethod);

        // The elapsed broker time is recorded, so a support case has evidence rather than a guess.
        Assert.NotNull(service.LastBrokerElapsed);
        Assert.True(service.LastBrokerElapsed >= TimeSpan.Zero);
        Assert.False(result.Value!.BrokerUsed);
    }

    [Fact]
    public async Task A_recognized_broker_failure_starts_the_browser()
    {
        var wam = new RecordingAcquirer
        {
            Throw = new MsalClientException("broker_not_available", "The broker is not available."),
        };
        var browser = new RecordingAcquirer { Result = AuthenticationResults.For(ApprovedTenant) };

        var service = Create(AuthenticationMode.Auto, wam, browser);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, browser.CallCount);
        Assert.Equal(FallbackReason.BrokerActivationFailure, service.LastFallbackReason);
    }

    [Fact]
    public async Task An_operator_cancellation_in_the_broker_is_reported_and_never_retried_in_the_browser()
    {
        // Falling back here would launch a second prompt after the operator said no.
        var wam = new RecordingAcquirer
        {
            Throw = new MsalClientException("authentication_canceled", "The user canceled."),
        };
        var browser = new RecordingAcquirer { Throw = new InvalidOperationException("the browser must not start") };

        var service = Create(AuthenticationMode.Auto, wam, browser);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.True(result.IsCanceled);
        Assert.Equal(0, browser.CallCount);
        Assert.Equal(Contracts.Cloud.AuthenticationState.Canceled, service.State);
    }

    [Fact]
    public async Task A_tenant_policy_answer_from_the_broker_is_not_retried_in_the_browser()
    {
        // A Conditional Access refusal is the tenant's answer. Silently retrying it in another browser
        // would obscure the real policy failure and cannot resolve it.
        var wam = new RecordingAcquirer
        {
            Throw = new MsalServiceException("AADSTS53003", "Access has been blocked by Conditional Access policies."),
        };
        var browser = new RecordingAcquirer { Throw = new InvalidOperationException("the browser must not start") };

        var service = Create(AuthenticationMode.Auto, wam, browser);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, browser.CallCount);
        Assert.Equal(Contracts.Cloud.AuthenticationState.Failed, service.State);
        Assert.Contains("AADSTS53003", result.Error!.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stale_broker_completion_cannot_replace_the_browser_result()
    {
        // The broker answers after the browser already adopted a session. Its attempt was retired, so its
        // result must be discarded rather than applied over the newer one.
        var wam = new RecordingAcquirer
        {
            Result = AuthenticationResults.For(ApprovedTenant, uniqueId: "broker-late"),
            Delay = TimeSpan.FromMilliseconds(700),
        };
        var browser = new RecordingAcquirer
        {
            Result = AuthenticationResults.For(ApprovedTenant, uniqueId: "browser-win"),
            Delay = TimeSpan.FromMilliseconds(100),
        };
        var detector = new TimedSurfaceDetector(TimeSpan.MaxValue);

        var service = Create(
            AuthenticationMode.Auto,
            wam,
            browser,
            detector,
            window: TimeSpan.FromMilliseconds(120));

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthenticationMethod.SystemBrowser, service.LastMethod);
        Assert.False(service.CurrentAccount!.BrokerUsed);

        // Wait past the late broker completion and re-check: state must not have moved.
        await Task.Delay(900);

        Assert.Equal(AuthenticationMethod.SystemBrowser, service.LastMethod);
        Assert.Equal("browser-win", service.CurrentAccount!.AccountObjectId);
        Assert.False(service.CurrentAccount.BrokerUsed);
        Assert.Equal(Contracts.Cloud.AuthenticationState.Authenticated, service.State);
        Assert.Contains(service.LastAttemptCheckpoints, c => c.Stage == "attempt-retired");
    }

    [Fact]
    public async Task WamOnly_never_starts_the_browser_even_when_the_broker_produces_no_ui()
    {
        var wam = new RecordingAcquirer { NeverCompletes = true };
        var browser = new RecordingAcquirer { Throw = new InvalidOperationException("the browser must not start") };

        var service = Create(
            AuthenticationMode.WamOnly,
            wam,
            browser);

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var result = await service.SignInAsync((IntPtr)1234, cancel.Token);

        Assert.Equal(0, browser.CallCount);
        Assert.True(result.IsCanceled);
    }

    [Fact]
    public async Task SystemBrowserOnly_never_starts_the_broker()
    {
        var wam = new RecordingAcquirer { Throw = new InvalidOperationException("the broker must not start") };
        var browser = new RecordingAcquirer { Result = AuthenticationResults.For(ApprovedTenant) };

        var service = Create(AuthenticationMode.SystemBrowserOnly, wam, browser);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, wam.CallCount);
        Assert.Equal(AuthenticationMethod.SystemBrowser, service.LastMethod);
        Assert.Null(service.LastBrokerElapsed);
    }

    [Fact]
    public async Task Auto_prefers_the_broker_over_the_browser()
    {
        var wam = new RecordingAcquirer { Result = AuthenticationResults.For(ApprovedTenant) };
        var browser = new RecordingAcquirer { Throw = new InvalidOperationException("the browser must not start") };

        await Create(AuthenticationMode.Auto, wam, browser).SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.Equal(1, wam.CallCount);
        Assert.Equal(0, browser.CallCount);
    }

    [Fact]
    public async Task A_cached_session_is_used_without_any_interaction()
    {
        var silent = new RequireInteractionSilentAcquirer { Result = AuthenticationResults.For(ApprovedTenant) };
        var wam = new RecordingAcquirer { Throw = new InvalidOperationException("no interaction is expected") };
        var browser = new RecordingAcquirer { Throw = new InvalidOperationException("no interaction is expected") };

        var service = Create(AuthenticationMode.Auto, wam, browser, silent: silent);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, wam.CallCount);
        Assert.Equal(0, browser.CallCount);
        Assert.Equal(AuthenticationMethod.None, service.LastMethod);
        Assert.False(result.Value!.BrokerUsed);
    }

    [Fact]
    public async Task A_browser_session_successfully_completes_and_is_recorded_as_the_browser()
    {
        var browser = new RecordingAcquirer { Result = AuthenticationResults.For(ApprovedTenant) };

        var service = Create(AuthenticationMode.SystemBrowserOnly, new RecordingAcquirer(), browser);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.BrokerUsed);
        Assert.Equal(ApprovedTenant, service.CurrentAccount!.TenantId);
        Assert.Equal(Contracts.Cloud.AuthenticationState.Authenticated, service.State);
    }

    [Fact]
    public async Task A_browser_cancellation_is_reported_as_canceled()
    {
        var browser = new RecordingAcquirer { Cancel = true };

        var service = Create(AuthenticationMode.SystemBrowserOnly, new RecordingAcquirer(), browser);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.True(result.IsCanceled);
        Assert.Equal(Contracts.Cloud.AuthenticationState.Canceled, service.State);
        Assert.Null(service.CurrentAccount);
    }

    [Fact]
    public async Task A_browser_authentication_failure_reaches_the_failed_state_without_a_token_in_the_message()
    {
        var browser = new RecordingAcquirer
        {
            Throw = new MsalServiceException("invalid_grant", "AADSTS70008: The refresh token has expired."),
        };

        var service = Create(AuthenticationMode.SystemBrowserOnly, new RecordingAcquirer(), browser);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Contracts.Cloud.AuthenticationState.Failed, service.State);
        Assert.DoesNotContain(AuthenticationResults.SyntheticAccessToken, result.Error!.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_browser_conditional_access_refusal_is_surfaced_as_the_real_policy_failure()
    {
        var browser = new RecordingAcquirer
        {
            Throw = new MsalServiceException("AADSTS53003", "Access has been blocked by Conditional Access policies."),
        };

        var service = Create(AuthenticationMode.SystemBrowserOnly, new RecordingAcquirer(), browser);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Contracts.Results.FailureCategory.Authentication, result.Error!.Category);

        // The tenant's own code is preserved so an administrator sees the real policy failure rather than
        // a generic message that invites a guess.
        Assert.Contains("AADSTS53003", result.Error.Summary, StringComparison.Ordinal);
        Assert.Equal(Contracts.Cloud.AuthenticationState.Failed, service.State);
    }

    [Fact]
    public async Task A_wrong_tenant_is_rejected_after_a_broker_sign_in()
    {
        var wam = new RecordingAcquirer { Result = AuthenticationResults.For(OtherTenant) };
        var browser = new RecordingAcquirer { Throw = new InvalidOperationException("the browser must not start") };

        var service = Create(AuthenticationMode.Auto, wam, browser);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, browser.CallCount);
        Assert.Equal(Contracts.Cloud.AuthenticationState.TenantMismatch, service.State);
        Assert.Null(service.CurrentAccount);
    }

    [Fact]
    public async Task A_wrong_tenant_is_rejected_after_a_browser_sign_in()
    {
        var browser = new RecordingAcquirer { Result = AuthenticationResults.For(OtherTenant) };

        var service = Create(AuthenticationMode.SystemBrowserOnly, new RecordingAcquirer(), browser);

        var result = await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Contracts.Cloud.AuthenticationState.TenantMismatch, service.State);
        Assert.Null(service.CurrentAccount);
    }

    [Fact]
    public async Task Signing_out_after_a_broker_session_clears_the_account_and_the_cache()
    {
        var clearer = new RecordingCacheClearer();
        var service = new PublicClientAuthenticationService(
            new MicrosoftIdentityConfigurationBuilder().Build(),
            application: null,
            windowHandleValidator: new StubWindowHandleValidator(),
            interactiveAcquirer: new RecordingAcquirer { Result = AuthenticationResults.For(ApprovedTenant) },
            silentAcquirer: new RequireInteractionSilentAcquirer(),
            browserAcquirer: new RecordingAcquirer(),
            mode: AuthenticationMode.WamOnly,
            cacheClearer: clearer);

        await service.SignInAsync((IntPtr)1234, CancellationToken.None);
        await service.SignOutAsync(CancellationToken.None);

        Assert.Equal(Contracts.Cloud.AuthenticationState.SignedOut, service.State);
        Assert.Null(service.CurrentAccount);
        Assert.Equal(1, clearer.CallCount);
    }

    [Fact]
    public async Task Signing_out_after_a_browser_session_clears_the_account_and_the_cache()
    {
        var clearer = new RecordingCacheClearer();
        var service = new PublicClientAuthenticationService(
            new MicrosoftIdentityConfigurationBuilder().Build(),
            application: null,
            windowHandleValidator: new StubWindowHandleValidator(),
            interactiveAcquirer: new RecordingAcquirer(),
            silentAcquirer: new RequireInteractionSilentAcquirer(),
            browserAcquirer: new RecordingAcquirer { Result = AuthenticationResults.For(ApprovedTenant) },
            mode: AuthenticationMode.SystemBrowserOnly,
            cacheClearer: clearer);

        await service.SignInAsync((IntPtr)1234, CancellationToken.None);
        await service.SignOutAsync(CancellationToken.None);

        Assert.Equal(Contracts.Cloud.AuthenticationState.SignedOut, service.State);

        // A cached account left behind would make the next silent acquisition succeed, so the sign-out
        // would look effective while the administrator stayed signed in.
        Assert.Equal(1, clearer.CallCount);
    }

    [Fact]
    public async Task A_cache_failure_still_leaves_the_session_signed_out()
    {
        var service = new PublicClientAuthenticationService(
            new MicrosoftIdentityConfigurationBuilder().Build(),
            application: null,
            windowHandleValidator: new StubWindowHandleValidator(),
            interactiveAcquirer: new RecordingAcquirer { Result = AuthenticationResults.For(ApprovedTenant) },
            silentAcquirer: new RequireInteractionSilentAcquirer(),
            browserAcquirer: new RecordingAcquirer(),
            mode: AuthenticationMode.WamOnly,
            cacheClearer: new RecordingCacheClearer { Throw = new IOException("cache is locked") });

        await service.SignInAsync((IntPtr)1234, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => service.SignOutAsync(CancellationToken.None));

        // The operator asked to sign out. Reporting the cache problem must not leave them visibly signed in.
        Assert.Equal(Contracts.Cloud.AuthenticationState.SignedOut, service.State);
        Assert.Null(service.CurrentAccount);
    }

    /// <summary>A detector that reports broker UI once a fixed delay has passed.</summary>
    private sealed class TimedSurfaceDetector(TimeSpan delay) : IBrokerSurfaceDetector
    {
        private readonly System.Diagnostics.Stopwatch _elapsed = System.Diagnostics.Stopwatch.StartNew();

        public bool IsBrokerUiVisible(IntPtr ownerWindowHandle) => _elapsed.Elapsed >= delay;
    }

    private sealed class RecordingCacheClearer : IAccountCacheClearer
    {
        public int CallCount { get; private set; }

        public Exception? Throw { get; set; }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Throw is null ? Task.CompletedTask : Task.FromException(Throw);
        }
    }
}
