using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ADEXO.Contracts.Cloud;
using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Mailbox;
using ADEXO.Core.Correlation;
using ADEXO.Graph.Orchestration;

namespace ADEXO.Desktop;

/// <summary>
/// The product shell: seven read-only sections plus the orchestration that fills them.
/// <para>
/// Sections are independent by construction. Each provider writes only its own sections, so a Graph
/// failure leaves the Active Directory tab intact, and a mailbox failure leaves the licence view intact.
/// There is no write command anywhere in this type.
/// </para>
/// </summary>
public sealed class ProductViewModel : INotifyPropertyChanged
{
    private readonly CloudSectionViewModel? _cloud;
    private readonly IMailboxProvider? _mailboxes;
    private string _statusLine = "No user is selected.";
    private string _sectionMessage = string.Empty;
    private CloudDirectoryUser? _cloudUser;
    private MailboxDetails? _mailbox;
    private MailboxUsage? _usage;
    private DelegationReadResult? _delegation;
    private string _correlationState = "Not evaluated";
    private readonly bool _hasExchangeProvider;
    private readonly Func<ExchangeOnline.IExchangeConnectionService>? _connectionService;
    private ExchangeOnline.ExchangeConnectionState _exchangeConnectionState = ExchangeOnline.ExchangeConnectionState.Disconnected;
    private bool _exchangeBusy;

    public ProductViewModel()
    {
        Overview = new DetailSectionViewModel("Overview");
        ActiveDirectory = new DetailSectionViewModel("Active Directory");
        Microsoft365 = new DetailSectionViewModel("Microsoft 365");
        Mailbox = new DetailSectionViewModel("Mailbox");
        EmailAddresses = new DetailSectionViewModel("Email addresses");
        Delegation = new DetailSectionViewModel("Delegation");
        Diagnostics = new DetailSectionViewModel("Diagnostics");
    }

    public ProductViewModel(
        CloudSectionViewModel cloud,
        IMailboxProvider? mailboxes,
        Func<Guid?> verifiedEntraObjectId,
        string? exchangeOrganization = null,
        Func<ExchangeOnline.IExchangeConnectionService>? connectionService = null)
        : this()
    {
        _cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
        _mailboxes = mailboxes;
        ArgumentNullException.ThrowIfNull(verifiedEntraObjectId);
        VerifiedEntraObjectId = verifiedEntraObjectId;
        ExchangeOrganization = exchangeOrganization;
        _connectionService = connectionService;
        _hasExchangeProvider = mailboxes is not null;

        ConnectExchangeCommand = new RelayCommand(
            async () => await ConnectExchangeAsync().ConfigureAwait(true),
            () => _hasExchangeProvider && !_exchangeBusy && ExchangeConnectionState != ExchangeOnline.ExchangeConnectionState.Connected);

        DisconnectExchangeCommand = new RelayCommand(
            async () => await DisconnectExchangeAsync().ConfigureAwait(true),
            () => _hasExchangeProvider && !_exchangeBusy && ExchangeConnectionState == ExchangeOnline.ExchangeConnectionState.Connected);

        RefreshAllCommand = new RelayCommand(
            async () => await RefreshMailboxAsync().ConfigureAwait(true),
            () => _mailboxes is not null && _correlationState == "Verified");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DetailSectionViewModel Overview { get; }

    public DetailSectionViewModel ActiveDirectory { get; }

    public DetailSectionViewModel Microsoft365 { get; }

    public DetailSectionViewModel Mailbox { get; }

    public DetailSectionViewModel EmailAddresses { get; }

    public DetailSectionViewModel Delegation { get; }

    public DetailSectionViewModel Diagnostics { get; }

    public ICommand? RefreshAllCommand { get; }

    /// <summary>Connects Exchange Online interactively. Never runs automatically at startup.</summary>
    public ICommand? ConnectExchangeCommand { get; }

    public ICommand? DisconnectExchangeCommand { get; }

    /// <summary>The Exchange organization to connect to, shown in the UI.</summary>
    public string? ExchangeOrganization { get; }

    public ExchangeOnline.ExchangeConnectionState ExchangeConnectionState
    {
        get => _exchangeConnectionState;
        private set
        {
            if (Set(ref _exchangeConnectionState, value))
            {
                OnPropertyChanged(nameof(ExchangeConnectionText));
                RaiseExchangeCommandState();
            }
        }
    }

    /// <summary>A single readable line for the connection state, used in the UI and diagnostics.</summary>
    public string ExchangeConnectionText => ExchangeConnectionState switch
    {
        ExchangeOnline.ExchangeConnectionState.Connected => "Connected",
        ExchangeOnline.ExchangeConnectionState.Connecting => "Connecting…",
        ExchangeOnline.ExchangeConnectionState.AccessDenied => "Access denied",
        ExchangeOnline.ExchangeConnectionState.AuthenticationFailed => "Authentication failed",
        ExchangeOnline.ExchangeConnectionState.ModuleUnavailable => "Exchange module unavailable",
        ExchangeOnline.ExchangeConnectionState.Failed => "Connection failed",
        _ => "Disconnected",
    };

    public async Task ConnectExchangeAsync()
    {
        if (_connectionService is null || string.IsNullOrWhiteSpace(ExchangeOrganization) || _exchangeBusy)
        {
            return;
        }

        _exchangeBusy = true;
        ExchangeConnectionState = ExchangeOnline.ExchangeConnectionState.Connecting;
        RaiseExchangeCommandState();

        try
        {
            var service = _connectionService();
            ExchangeConnectionState = await service.ConnectAsync(ExchangeOrganization!, CancellationToken.None).ConfigureAwait(true);

            if (ExchangeConnectionState == ExchangeOnline.ExchangeConnectionState.Connected)
            {
                // Only now can a mailbox be read, and only for a verified identity.
                await RefreshMailboxAsync().ConfigureAwait(true);
            }
            else
            {
                SectionMessage = $"Exchange Online: {ExchangeConnectionText}.";
            }
        }
        finally
        {
            _exchangeBusy = false;
            RaiseExchangeCommandState();
        }
    }

    public async Task DisconnectExchangeAsync()
    {
        if (_connectionService is null || _exchangeBusy)
        {
            return;
        }

        _exchangeBusy = true;
        RaiseExchangeCommandState();

        try
        {
            await _connectionService().DisconnectAsync(CancellationToken.None).ConfigureAwait(true);
            ExchangeConnectionState = ExchangeOnline.ExchangeConnectionState.Disconnected;

            _mailbox = null;
            _usage = null;
            _delegation = null;
            ProductSections.BuildMailbox(Mailbox, null, null);
            ProductSections.BuildEmailAddresses(EmailAddresses, null);
            ProductSections.BuildDelegation(Delegation, null);
        }
        finally
        {
            _exchangeBusy = false;
            RaiseExchangeCommandState();
        }
    }

    private void RaiseExchangeCommandState()
    {
        (ConnectExchangeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DisconnectExchangeCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>Supplies the Entra object identifier only when correlation is verified. Null otherwise.</summary>
    public Func<Guid?>? VerifiedEntraObjectId { get; }

    public string StatusLine
    {
        get => _statusLine;
        private set => Set(ref _statusLine, value);
    }

    /// <summary>A single sanitized line describing the last operation. Never an exception dump.</summary>
    public string SectionMessage
    {
        get => _sectionMessage;
        private set => Set(ref _sectionMessage, value);
    }

    public string CorrelationState
    {
        get => _correlationState;
        private set => Set(ref _correlationState, value);
    }

    public CloudDirectoryUser? CloudUser => _cloudUser;

    public MailboxDetails? MailboxDetails => _mailbox;

    /// <summary>
    /// Rebuilds every section from the currently known state. Called after an AD load, after a cloud
    /// operation, and by the refresh command.
    /// </summary>
    public void Rebuild(VerifiedDirectoryUser? adUser, CloudSectionResult? cloudResult, string? cloudMessage = null)
    {
        _cloudUser = cloudResult?.ShowsUserData == true ? cloudResult.User : null;
        CorrelationState = DescribeCorrelation(cloudResult);

        ProductSections.BuildOverview(Overview, adUser, _cloudUser, _mailbox, CorrelationState);
        ProductSections.BuildActiveDirectory(ActiveDirectory, adUser);
        ProductSections.BuildMicrosoft365(
            Microsoft365,
            cloudResult?.Account is not null,
            cloudResult?.Tenant?.ApprovedTenantId,
            cloudResult?.Account?.Username,
            CorrelationState,
            _cloudUser);
        ProductSections.BuildMailbox(Mailbox, _mailbox, _usage);
        ProductSections.BuildEmailAddresses(EmailAddresses, _mailbox);
        ProductSections.BuildDelegation(Delegation, _delegation);

        StatusLine = adUser is null ? "No user is selected." : adUser.DisplayName ?? adUser.SamAccountName ?? "User";
        SectionMessage = cloudMessage ?? string.Empty;

        RefreshMailboxCommandState();
    }

    /// <summary>
    /// Reads the mailbox and its usage for a verified identity. Refuses any other correlation state, so a
    /// mailbox can never be read for an unverified object.
    /// </summary>
    public async Task RefreshMailboxAsync()
    {
        if (_mailboxes is null || VerifiedEntraObjectId?.Invoke() is not { } entraObjectId)
        {
            _mailbox = null;
            _usage = null;
            _delegation = null;
            ProductSections.BuildMailbox(Mailbox, null, null);
            ProductSections.BuildEmailAddresses(EmailAddresses, null);
            ProductSections.BuildDelegation(Delegation, null);
            return;
        }

        Mailbox.BeginLoad("Reading mailbox…");
        EmailAddresses.BeginLoad("Reading mailbox…");

        try
        {
            var mailbox = await _mailboxes.GetMailboxAsync(entraObjectId, CancellationToken.None).ConfigureAwait(true);
            if (!mailbox.IsSuccess || mailbox.Value is null)
            {
                _mailbox = null;
                _usage = null;
                _delegation = null;
                Mailbox.SetEmpty(mailbox.Error?.Summary ?? "The mailbox could not be read.");
                EmailAddresses.SetEmpty("No email addresses are available.");
                Delegation.SetEmpty("Delegation could not be read.");
                SectionMessage = mailbox.Error?.Summary ?? string.Empty;
                return;
            }

            _mailbox = mailbox.Value;

            _usage = null;
            if (_mailbox.Kind is MailboxKind.UserMailbox or MailboxKind.SharedMailbox)
            {
                var usage = await _mailboxes.GetUsageAsync(_mailbox, CancellationToken.None).ConfigureAwait(true);
                _usage = usage.IsSuccess ? usage.Value : null;
            }

            var delegation = await _mailboxes.GetDelegationsAsync(_mailbox, CancellationToken.None).ConfigureAwait(true);
            _delegation = delegation.IsSuccess ? delegation.Value : null;

            ProductSections.BuildMailbox(Mailbox, _mailbox, _usage);
            ProductSections.BuildEmailAddresses(EmailAddresses, _mailbox);
            ProductSections.BuildDelegation(Delegation, _delegation);
            ProductSections.BuildOverview(Overview, null, _cloudUser, _mailbox, CorrelationState);
        }
        finally
        {
            Mailbox.EndLoad();
            EmailAddresses.EndLoad();
        }
    }

    /// <summary>Rebuilds the diagnostics section from provider capabilities and the last known state.</summary>
    public void BuildDiagnostics(
        IReadOnlyList<Contracts.Capabilities.ProviderCapability> capabilities,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? correlationIds = null,
        TimeSpan? elapsed = null)
    {
        var provider = new DetailGroup { Title = "Providers" };
        foreach (var capability in capabilities)
        {
            var state = !capability.Available ? "Unavailable"
                : !capability.Authenticated ? "Not authenticated"
                : !capability.Authorized ? $"Missing permission: {capability.MissingPermission}"
                : capability.CanRead ? "Read-only ready"
                : "No read access";

            provider.Rows.Add(DetailProjection.Row(
                capability.Provider.ToString(),
                state,
                capability.CanRead && capability.Authorized ? DetailSeverity.Good : DetailSeverity.Warning));
        }

        var state2 = new DetailGroup { Title = "State" };
        state2.Rows.Add(DetailProjection.Row("Cloud correlation", CorrelationState));
        state2.Rows.Add(DetailProjection.Row("Mailbox", _mailbox?.Kind.ToString() ?? "Not read"));
        state2.Rows.Add(DetailProjection.Row("Exchange Online", ExchangeConnectionText));
        state2.Rows.Add(DetailProjection.Row("Elapsed", elapsed?.ToString() ?? "—"));

        var groups = new List<DetailGroup> { provider, state2 };

        if (_cloud is not null)
        {
            // Which authentication mechanism actually ran, and why it fell back. An administrator
            // needs this to understand the session's security properties.
            var auth = new DetailGroup { Title = "Authentication method" };
            auth.Rows.Add(DetailProjection.Row("Preferred", _cloud.PreferredMethodText));
            auth.Rows.Add(DetailProjection.Row("Actual", _cloud.ActualMethodText));
            auth.Rows.Add(DetailProjection.Row("Fallback reason", _cloud.FallbackReasonText,
                _cloud.HasFallback ? DetailSeverity.Warning : DetailSeverity.Muted));
            auth.Rows.Add(DetailProjection.Row("Broker start elapsed", _cloud.BrokerElapsedText));
            groups.Add(auth);
        }

        if (errors is { Count: > 0 })
        {
            var errorGroup = new DetailGroup { Title = "Sanitized errors" };
            foreach (var error in errors)
            {
                errorGroup.Rows.Add(DetailProjection.Row("error", error, DetailSeverity.Warning));
            }

            groups.Add(errorGroup);
        }

        if (correlationIds is { Count: > 0 })
        {
            var idGroup = new DetailGroup { Title = "Correlation identifiers" };
            foreach (var id in correlationIds)
            {
                idGroup.Rows.Add(DetailProjection.Row("id", id, DetailSeverity.Muted));
            }

            groups.Add(idGroup);
        }

        Diagnostics.Populate([.. groups]);
    }

    private void RefreshMailboxCommandState()
    {
        if (RefreshAllCommand is RelayCommand command)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private static string DescribeCorrelation(CloudSectionResult? result) => result?.State switch
    {
        CloudSectionState.Verified => "Verified",
        CloudSectionState.Unmatched => "Unmatched",
        CloudSectionState.Ambiguous => "Ambiguous",
        CloudSectionState.Conflict => "Conflict",
        CloudSectionState.TenantMismatch => "Tenant mismatch",
        CloudSectionState.TenantVerified => "Tenant verified; user not correlated",
        CloudSectionState.SignedOut => "Not signed in",
        CloudSectionState.NotConfigured => "Not configured",
        null => "Not evaluated",
        _ => result.State.ToString(),
    };

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
