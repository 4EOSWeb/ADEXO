using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Launch;
using ADEXO.Contracts.Providers;
using ADEXO.Contracts.Results;
using ADEXO.Contracts.Time;
using ADEXO.Core.Launch;

namespace ADEXO.Desktop.Tests;

/// <summary>
/// Regression coverage for the Windows domain lab defect: after a verified load the
/// Refresh command stayed disabled, so a moved object could never be re-read by GUID.
/// These tests drive the real <see cref="System.Windows.Input.ICommand"/> surface
/// instead of calling <c>RefreshAsync</c> directly, because calling the method skips
/// <c>CanExecute</c> entirely and that is how the defect survived CI.
/// </summary>
public sealed class RefreshCommandTests
{
    // The moved-object test uses the canonical GUID captured in the lab so the
    // assertion mirrors what the live directory returned. It is test data only.
    private static readonly Guid LabGuid = Guid.Parse("a116a060-4fc6-482b-be01-494c2cf4606e");

    private static readonly string[] UserLaunch = ["LDAP://CN=User,OU=Lab,DC=example,DC=com", "user"];

    [Fact]
    public void Refresh_is_disabled_before_a_verified_object_is_loaded()
    {
        var directory = new ControllableDirectory();
        var model = CreateModel(directory);

        Assert.False(model.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task Refresh_raises_can_execute_changed_and_becomes_enabled_after_a_verified_load()
    {
        var directory = new ControllableDirectory { User = Sample() };
        var model = CreateModel(directory);
        var raised = 0;
        model.RefreshCommand.CanExecuteChanged += (_, _) => raised++;

        Assert.False(model.RefreshCommand.CanExecute(null));

        await model.StartAsync(UserLaunch);

        Assert.Equal(ReadOnlyViewState.Loaded, model.State);
        Assert.True(raised > 0, "RefreshCommand did not raise CanExecuteChanged after identity verification.");
        Assert.True(model.RefreshCommand.CanExecute(null));
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task Refresh_executes_through_the_command_and_requests_the_object_guid()
    {
        var directory = new ControllableDirectory { User = Sample() };
        var model = CreateModel(directory);
        await model.StartAsync(UserLaunch);

        Assert.True(model.RefreshCommand.CanExecute(null));
        model.RefreshCommand.Execute(null);

        await WaitUntilAsync(() => directory.RefreshGuids.Count > 0);
        await WaitUntilAsync(() => !model.IsBusy);

        Assert.Single(directory.RefreshGuids);
        Assert.Equal(Sample().ObjectGuid, directory.RefreshGuids[0]);
    }

    [Fact]
    public async Task Refresh_never_uses_the_original_launch_path()
    {
        var directory = new ControllableDirectory { User = Sample() };
        var model = CreateModel(directory);
        await model.StartAsync(UserLaunch);
        Assert.Equal(1, directory.ResolveCalls);

        model.RefreshCommand.Execute(null);
        await WaitUntilAsync(() => directory.RefreshGuids.Count > 0);
        await WaitUntilAsync(() => !model.IsBusy);

        // A path-based refresh would have to resolve again. It must not.
        Assert.Equal(1, directory.ResolveCalls);
        Assert.Equal(Sample().ObjectGuid, directory.RefreshGuids[0]);
    }

    [Fact]
    public async Task Refresh_is_disabled_while_the_refresh_is_in_flight()
    {
        var directory = new ControllableDirectory { User = Sample() };
        var model = CreateModel(directory);
        await model.StartAsync(UserLaunch);

        directory.HoldNextRefresh = true;
        model.RefreshCommand.Execute(null);
        await WaitUntilAsync(() => model.IsBusy);

        Assert.True(model.IsBusy);
        Assert.False(model.RefreshCommand.CanExecute(null));

        directory.ReleaseHeldRefresh();
        await WaitUntilAsync(() => !model.IsBusy);

        Assert.True(model.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task Can_execute_changed_is_raised_when_the_busy_state_changes()
    {
        var directory = new ControllableDirectory { User = Sample() };
        var model = CreateModel(directory);
        await model.StartAsync(UserLaunch);

        var raised = 0;
        model.RefreshCommand.CanExecuteChanged += (_, _) => raised++;

        directory.HoldNextRefresh = true;
        model.RefreshCommand.Execute(null);
        await WaitUntilAsync(() => model.IsBusy);
        var raisedWhileStarting = raised;

        directory.ReleaseHeldRefresh();
        await WaitUntilAsync(() => !model.IsBusy);

        Assert.True(raisedWhileStarting > 0, "CanExecuteChanged was not raised when the load started.");
        Assert.True(raised > raisedWhileStarting, "CanExecuteChanged was not raised when the load completed.");
    }

    [Fact]
    public async Task Refresh_stays_disabled_when_initial_verification_fails()
    {
        var directory = new ControllableDirectory
        {
            Error = OperationError.Create(FailureCategory.VerificationFailed, "GUID rebind did not return the same object."),
        };
        var model = CreateModel(directory);

        await model.StartAsync(UserLaunch);

        Assert.Equal(ReadOnlyViewState.IdentityVerificationFailed, model.State);
        Assert.False(model.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task Refresh_stays_disabled_after_cancellation_before_identity_verification()
    {
        var directory = new ControllableDirectory { User = Sample(), HoldNextResolve = true };
        var model = CreateModel(directory);

        var loading = model.StartAsync(UserLaunch);
        await WaitUntilAsync(() => model.IsBusy);
        model.Cancel();
        directory.ReleaseHeldResolve();
        await loading;

        Assert.Equal(ReadOnlyViewState.Canceled, model.State);
        Assert.False(model.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_canceled_load_discards_its_result_and_keeps_refresh_disabled_until_a_verified_load()
    {
        // The view model serializes loads behind a gate, so the stale-completion
        // case is exercised by completing the canceled load and then loading again.
        // The discarded result must not leave a verified GUID behind.
        var directory = new ControllableDirectory { User = Sample(), HoldNextResolve = true };
        var model = CreateModel(directory);

        var canceled = model.StartAsync(UserLaunch);
        await WaitUntilAsync(() => model.IsBusy);
        model.Cancel();
        directory.ReleaseHeldResolve();
        await canceled;

        Assert.Equal(ReadOnlyViewState.Canceled, model.State);
        Assert.False(model.RefreshCommand.CanExecute(null));
        Assert.Empty(directory.RefreshGuids);

        // A later genuine load establishes the identity for its own object only.
        await model.StartAsync(UserLaunch);

        Assert.Equal(ReadOnlyViewState.Loaded, model.State);
        Assert.True(model.RefreshCommand.CanExecute(null));

        model.RefreshCommand.Execute(null);
        await WaitUntilAsync(() => directory.RefreshGuids.Count > 0);
        await WaitUntilAsync(() => !model.IsBusy);

        Assert.All(directory.RefreshGuids, guid => Assert.Equal(Sample().ObjectGuid, guid));
    }

    [Fact]
    public async Task Refreshing_a_moved_object_keeps_the_guid_and_loads_the_new_distinguished_name()
    {
        var directory = new ControllableDirectory
        {
            User = SampleWith(LabGuid, "CN=User,OU=Lab,DC=example,DC=com", "example.com/Lab/User"),
            MovedUser = SampleWith(LabGuid, "CN=User,OU=Lab-Moved,DC=example,DC=com", "example.com/Lab-Moved/User"),
        };
        var model = CreateModel(directory);

        await model.StartAsync(UserLaunch);
        Assert.Equal("CN=User,OU=Lab,DC=example,DC=com", model.User?.DistinguishedName);

        model.RefreshCommand.Execute(null);
        await WaitUntilAsync(() => directory.RefreshGuids.Count > 0);
        await WaitUntilAsync(() => !model.IsBusy);

        Assert.Equal(LabGuid, directory.RefreshGuids[0]);
        Assert.Equal(LabGuid, model.User?.ObjectGuid);
        Assert.Equal("CN=User,OU=Lab-Moved,DC=example,DC=com", model.User?.DistinguishedName);
        Assert.Equal("example.com/Lab-Moved/User", model.User?.CanonicalName);
        Assert.Equal(ReadOnlyViewState.Loaded, model.State);
        Assert.Equal(1, directory.ResolveCalls);
    }

    [Fact]
    public async Task A_refresh_exception_becomes_the_sanitized_failure_state()
    {
        var directory = new ControllableDirectory { User = Sample() };
        var model = CreateModel(directory);
        await model.StartAsync(UserLaunch);

        directory.ThrowOnNextRefresh = true;
        model.RefreshCommand.Execute(null);
        await WaitUntilAsync(() => model.State == ReadOnlyViewState.UnknownFailure);

        Assert.Equal(ReadOnlyViewState.UnknownFailure, model.State);
        Assert.Equal("The directory request failed.", model.FailureMessage);
        Assert.DoesNotContain("CN=User", model.FailureMessage, StringComparison.Ordinal);

        // The identity was still verified before the failed refresh, so a retry stays
        // available. Only an unverified identity may disable Refresh.
        Assert.True(model.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task Repeated_refresh_invocations_do_not_overlap()
    {
        var directory = new ControllableDirectory { User = Sample() };
        var model = CreateModel(directory);
        await model.StartAsync(UserLaunch);

        directory.HoldNextRefresh = true;
        model.RefreshCommand.Execute(null);
        await WaitUntilAsync(() => model.IsBusy);

        // A second invocation while the first is in flight must not start a
        // concurrent directory request.
        model.RefreshCommand.Execute(null);
        await Task.Delay(75);

        Assert.Equal(1, directory.MaxConcurrentRefreshes);

        directory.ReleaseHeldRefresh();
        await WaitUntilAsync(() => !model.IsBusy);
    }

    [Fact]
    public async Task Cancel_command_tracks_the_busy_state_through_can_execute()
    {
        var directory = new ControllableDirectory { User = Sample(), HoldNextResolve = true };
        var model = CreateModel(directory);
        var raised = 0;
        model.CancelCommand.CanExecuteChanged += (_, _) => raised++;

        Assert.False(model.CancelCommand.CanExecute(null));

        var loading = model.StartAsync(UserLaunch);
        await WaitUntilAsync(() => model.IsBusy);

        Assert.True(raised > 0, "CancelCommand did not raise CanExecuteChanged when the load started.");
        Assert.True(model.CancelCommand.CanExecute(null));

        model.CancelCommand.Execute(null);
        directory.ReleaseHeldResolve();
        await loading;

        Assert.False(model.CancelCommand.CanExecute(null));
    }

    private static MainViewModel CreateModel(ControllableDirectory directory) =>
        new(new AducLaunchRequestParser(new FixedClock()), directory, new FixedClock());

    private static VerifiedDirectoryUser Sample() =>
        SampleWith(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), "CN=User,OU=Lab,DC=example,DC=com", "example.com/Lab/User");

    private static VerifiedDirectoryUser SampleWith(Guid guid, string distinguishedName, string canonicalName) => new()
    {
        ObjectGuid = guid,
        DistinguishedName = distinguishedName,
        CanonicalName = canonicalName,
        ObjectClass = "user",
        DisplayName = "Example User",
        Stage = IdentityStage.DisplayDataLoaded,
    };

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var started = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - started > timeoutMilliseconds)
            {
                throw new TimeoutException("The expected view-model condition was not reached in time.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class ControllableDirectory : IActiveDirectoryProvider
    {
        private readonly object _sync = new();
        private int _concurrentRefreshes;
        private TaskCompletionSource<bool>? _heldRefresh;
        private TaskCompletionSource<bool>? _heldResolve;

        public VerifiedDirectoryUser? User { get; init; }

        public VerifiedDirectoryUser? MovedUser { get; init; }

        public OperationError? Error { get; init; }

        public int ResolveCalls { get; private set; }

        public List<Guid> RefreshGuids { get; } = [];

        public int MaxConcurrentRefreshes { get; private set; }

        public bool HoldNextRefresh { get; set; }

        public bool HoldNextResolve { get; set; }

        public bool ThrowOnNextRefresh { get; set; }

        public void ReleaseHeldRefresh() => _heldRefresh?.TrySetResult(true);

        public void ReleaseHeldResolve() => _heldResolve?.TrySetResult(true);

        public Task<Contracts.Capabilities.ProviderCapability> GetCapabilityAsync(
            Contracts.Features.KnownFeatureId feature,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ProviderResult<TrustedDirectoryEndpoint>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ProviderResult<TrustedDirectoryEndpoint>.Success(
                new TrustedDirectoryEndpoint("dc01.example.com", "DC=example,DC=com", "CN=Configuration,DC=example,DC=com", null)));

        public async Task<ProviderResult<VerifiedDirectoryUser>> ResolveUserAsync(
            AducLaunchRequest request,
            CancellationToken cancellationToken)
        {
            ResolveCalls++;
            if (HoldNextResolve)
            {
                HoldNextResolve = false;
                var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _heldResolve = gate;
                await gate.Task.ConfigureAwait(false);
            }

            return Result(User);
        }

        public async Task<ProviderResult<VerifiedDirectoryUser>> RefreshByObjectGuidAsync(
            Guid objectGuid,
            CancellationToken cancellationToken)
        {
            if (ThrowOnNextRefresh)
            {
                ThrowOnNextRefresh = false;
                throw new InvalidOperationException("The directory request failed.");
            }

            lock (_sync)
            {
                RefreshGuids.Add(objectGuid);
                _concurrentRefreshes++;
                if (_concurrentRefreshes > MaxConcurrentRefreshes)
                {
                    MaxConcurrentRefreshes = _concurrentRefreshes;
                }
            }

            try
            {
                if (HoldNextRefresh)
                {
                    HoldNextRefresh = false;
                    var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _heldRefresh = gate;
                    await gate.Task.ConfigureAwait(false);
                }

                return Result(MovedUser ?? User);
            }
            finally
            {
                lock (_sync)
                {
                    _concurrentRefreshes--;
                }
            }
        }

        private ProviderResult<VerifiedDirectoryUser> Result(VerifiedDirectoryUser? user) =>
            Error is null && user is not null
                ? ProviderResult<VerifiedDirectoryUser>.Success(user)
                : ProviderResult<VerifiedDirectoryUser>.Failed(
                    Error ?? OperationError.Create(FailureCategory.Unknown, "failed"));
    }
}
