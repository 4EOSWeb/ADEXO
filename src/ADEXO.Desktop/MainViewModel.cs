using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Launch;
using ADEXO.Contracts.Providers;
using ADEXO.Contracts.Results;
using ADEXO.Contracts.Time;
using ADEXO.Core.Launch;

namespace ADEXO.Desktop;

public enum ReadOnlyViewState
{
    NoLaunchRequest,
    Validating,
    Connecting,
    Resolving,
    VerifyingGuid,
    Loading,
    Loaded,
    UnsupportedClass,
    NotFound,
    AccessDenied,
    DomainUnavailable,
    ValidationFailure,
    Canceled,
    IdentityVerificationFailed,
    UnknownFailure,
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IAducLaunchRequestParser _parser;
    private readonly IActiveDirectoryProvider _directory;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RelayCommand _refreshCommand;
    private readonly RelayCommand _cancelCommand;
    private int _generation;
    private Guid? _verifiedGuid;
    private string _status = "No user is selected.";
    private string _failure = string.Empty;
    private VerifiedDirectoryUser? _user;
    private ReadOnlyViewState _state = ReadOnlyViewState.NoLaunchRequest;
    private string _guidVerification = "Not run";
    private string _providerStatus = "Not connected";
    private TimeSpan _elapsed;

    public MainViewModel(IAducLaunchRequestParser parser, IActiveDirectoryProvider directory, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(clock);
        _parser = parser;
        _directory = directory;
        _clock = clock;
        Cloud = new CloudSectionViewModel();
        RefreshCommand = _refreshCommand = new RelayCommand(async () => await RefreshAsync().ConfigureAwait(true), () => _verifiedGuid is not null && !IsBusy);
        CancelCommand = _cancelCommand = new RelayCommand(Cancel, () => IsBusy);
        CopyDiagnosticsCommand = new RelayCommand(() => CopiedText = BuildDiagnostics(includeSensitive: false));
        CopyDetailedDiagnosticsCommand = new RelayCommand(() => CopiedText = BuildDiagnostics(includeSensitive: true));
    }

    /// <summary>
    /// The read-only Microsoft 365 section. It owns its own state and never writes AD state, so a
    /// cloud failure cannot unload the Active Directory user.
    /// </summary>
    public CloudSectionViewModel Cloud { get; private set; } = new();

    /// <summary>
    /// The seven read-only product sections. Populated from the AD load and the cloud reads, and rebuilt
    /// whenever either changes.
    /// </summary>
    public ProductViewModel Product { get; private set; } = new();

    /// <summary>
    /// Attaches the configured cloud section. Called once by the composition root after the window
    /// exists, so the broker dialog can be parented to a real handle.
    /// </summary>
    public void AttachCloud(CloudSectionViewModel cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        Cloud = cloud;
        OnPropertyChanged(nameof(Cloud));
    }

    /// <summary>
    /// Attaches the product shell. The shell keeps its own sections, so AD data stays readable while the
    /// cloud and Exchange providers are signed out, failing, or unavailable.
    /// </summary>
    public void AttachProduct(ProductViewModel product)
    {
        ArgumentNullException.ThrowIfNull(product);
        Product = product;
        OnPropertyChanged(nameof(Product));
        Product.Rebuild(User, null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand RefreshCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand CopyDiagnosticsCommand { get; }

    public ICommand CopyDetailedDiagnosticsCommand { get; }

    public ReadOnlyViewState State
    {
        get => _state;
        private set => Set(ref _state, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string FailureMessage
    {
        get => _failure;
        private set => Set(ref _failure, value);
    }

    public VerifiedDirectoryUser? User
    {
        get => _user;
        private set => Set(ref _user, value);
    }

    public string GuidVerification
    {
        get => _guidVerification;
        private set => Set(ref _guidVerification, value);
    }

    public string ProviderStatus
    {
        get => _providerStatus;
        private set => Set(ref _providerStatus, value);
    }

    public string Elapsed => _elapsed.ToString();

    public string? CopiedText { get; private set; }

    public bool IsBusy => State is ReadOnlyViewState.Validating or ReadOnlyViewState.Connecting or ReadOnlyViewState.Resolving or ReadOnlyViewState.VerifyingGuid or ReadOnlyViewState.Loading;

    public bool ShowUser => State == ReadOnlyViewState.Loaded && User is not null;

    public async Task StartAsync(IReadOnlyList<string> arguments)
    {
        var generation = Interlocked.Increment(ref _generation);
        var started = _clock.UtcNow;
        State = ReadOnlyViewState.Validating;
        var parsed = _parser.Parse(arguments, arguments.Count == 0 ? LaunchSource.Unknown : LaunchSource.AducStaticContextMenu);
        if (parsed.IsEmptyLaunch)
        {
            State = ReadOnlyViewState.NoLaunchRequest;
            Status = "Phase 2 opens a user from Active Directory Users and Computers.";
            return;
        }

        if (!parsed.Succeeded || parsed.Request is null)
        {
            State = parsed.FailureCode == "unsupported-class"
                ? ReadOnlyViewState.UnsupportedClass
                : ReadOnlyViewState.ValidationFailure;
            FailureMessage = parsed.FailureMessage ?? "The launch request was rejected.";
            GuidVerification = "Not run";
            return;
        }

        await LoadResolvedAsync(generation, started, token => _directory.ResolveUserAsync(parsed.Request, token)).ConfigureAwait(true);
    }

    public async Task RefreshAsync()
    {
        if (_verifiedGuid is null)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _generation);
        var guid = _verifiedGuid.Value;
        await LoadResolvedAsync(generation, _clock.UtcNow, token => _directory.RefreshByObjectGuidAsync(guid, token)).ConfigureAwait(true);
    }

    public void Cancel()
    {
        Interlocked.Increment(ref _generation);
        State = ReadOnlyViewState.Canceled;
        Status = "The load was stopped. An in-flight directory request is bounded by its timeout and its result will be discarded. The network request itself was not reported as canceled.";
        GuidVerification = "Discarded";
    }

    private async Task LoadResolvedAsync(int generation, DateTimeOffset started, Func<CancellationToken, Task<ProviderResult<VerifiedDirectoryUser>>> load)
    {
        using var cancellation = new CancellationTokenSource();
        await _gate.WaitAsync(cancellation.Token).ConfigureAwait(true);
        try
        {
            if (generation != _generation)
            {
                return;
            }

            State = ReadOnlyViewState.Connecting;
            State = ReadOnlyViewState.Resolving;
            State = ReadOnlyViewState.VerifyingGuid;
            State = ReadOnlyViewState.Loading;
            var result = await load(cancellation.Token).ConfigureAwait(true);
            if (generation != _generation)
            {
                ProviderStatus = "A directory result was discarded because a newer request is current. The network request itself was not reported as canceled.";
                return;
            }

            _elapsed = _clock.UtcNow - started;
            OnPropertyChanged(nameof(Elapsed));
            Apply(result);
        }
        catch (Exception)
        {
            if (generation == _generation)
            {
                State = ReadOnlyViewState.UnknownFailure;
                FailureMessage = "The directory request failed.";
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Apply(ProviderResult<VerifiedDirectoryUser> result)
    {
        if (result.IsCanceled)
        {
            State = ReadOnlyViewState.Canceled;
            return;
        }

        if (!result.IsSuccess || result.Value is null)
        {
            User = null;
            GuidVerification = "Failed";
            State = result.Error?.Category switch
            {
                FailureCategory.NotFound => ReadOnlyViewState.NotFound,
                FailureCategory.Authorization => ReadOnlyViewState.AccessDenied,
                FailureCategory.Network or FailureCategory.ProviderUnavailable or FailureCategory.Timeout => ReadOnlyViewState.DomainUnavailable,
                FailureCategory.Unsupported => ReadOnlyViewState.UnsupportedClass,
                FailureCategory.VerificationFailed => ReadOnlyViewState.IdentityVerificationFailed,
                _ => ReadOnlyViewState.UnknownFailure,
            };
            FailureMessage = result.Error?.Summary ?? "The user could not be loaded.";
            return;
        }

        User = result.Value;
        _verifiedGuid = result.Value.ObjectGuid;
        GuidVerification = "Passed";
        ProviderStatus = "Read-only";
        Status = result.Value.DisplayName ?? result.Value.SamAccountName ?? "User";
        State = ReadOnlyViewState.Loaded;
    }

    public string BuildDiagnostics(bool includeSensitive)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ADEXO read-only diagnostics");
        builder.AppendLine("Result: " + State);
        builder.AppendLine("GUID verification: " + GuidVerification);
        builder.AppendLine("Provider: " + ProviderStatus);
        builder.AppendLine("Elapsed: " + Elapsed);
        builder.AppendLine("Attributes: objectGUID, distinguishedName, objectClass, account, contact, organization, mail metadata");
        if (includeSensitive && User is not null)
        {
            builder.AppendLine("Sensitive directory identifiers:");
            builder.AppendLine("Distinguished name: " + User.DistinguishedName);
        }

        return builder.ToString();
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        OnPropertyChanged(name);
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowUser));
    }

    private void OnPropertyChanged(string? name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(State) || name == nameof(IsBusy))
        {
            Invalidate(_refreshCommand);
            Invalidate(_cancelCommand);
        }
    }

    /// <summary>
    /// WPF asks a command for <c>CanExecute</c> once, at bind time, and only asks again when the
    /// command raises <see cref="ICommand.CanExecuteChanged"/>. The Refresh and Cancel predicates
    /// depend on the verified GUID and the busy state, so a change in either must invalidate them.
    /// Relying on <c>CommandManager.RequerySuggested</c> instead would make the enabled state depend
    /// on WPF's input timing and would not be deterministic under test.
    /// </summary>
    private static void Invalidate(RelayCommand? command) => command?.RaiseCanExecuteChanged();
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
