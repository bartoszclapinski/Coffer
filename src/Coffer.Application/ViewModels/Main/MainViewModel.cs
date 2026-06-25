using System.Reflection;
using Coffer.Application.ViewModels.Alerts;
using Coffer.Application.ViewModels.Chat;
using Coffer.Application.ViewModels.Dashboard;
using Coffer.Application.ViewModels.Goals;
using Coffer.Application.ViewModels.Import;
using Coffer.Application.ViewModels.Settings;
using Coffer.Application.ViewModels.Transactions;
using Coffer.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Main;

/// <summary>
/// Shell view-model behind the post-login <c>MainWindow</c>. Hosts the sidebar
/// navigation and swaps <see cref="CurrentPage"/> between the section view-models
/// (Dashboard, Import, Transactions, Settings). Still owns the logout command and
/// <see cref="LoggedOut"/> event that <c>App.axaml.cs</c> subscribes to in order to
/// swap windows.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ILoginService _loginService;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardActive))]
    [NotifyPropertyChangedFor(nameof(IsImportActive))]
    [NotifyPropertyChangedFor(nameof(IsTransactionsActive))]
    [NotifyPropertyChangedFor(nameof(IsChatActive))]
    [NotifyPropertyChangedFor(nameof(IsAlertsActive))]
    [NotifyPropertyChangedFor(nameof(IsAdvisorActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    private ObservableObject? _currentPage;

    public MainViewModel(
        DashboardViewModel dashboardViewModel,
        ImportViewModel importViewModel,
        TransactionsViewModel transactionsViewModel,
        ChatViewModel chatViewModel,
        AlertsViewModel alertsViewModel,
        GoalsViewModel goalsViewModel,
        SettingsViewModel settingsViewModel,
        ILoginService loginService,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(dashboardViewModel);
        ArgumentNullException.ThrowIfNull(importViewModel);
        ArgumentNullException.ThrowIfNull(transactionsViewModel);
        ArgumentNullException.ThrowIfNull(chatViewModel);
        ArgumentNullException.ThrowIfNull(alertsViewModel);
        ArgumentNullException.ThrowIfNull(goalsViewModel);
        ArgumentNullException.ThrowIfNull(settingsViewModel);
        ArgumentNullException.ThrowIfNull(loginService);
        ArgumentNullException.ThrowIfNull(logger);

        Dashboard = dashboardViewModel;
        Import = importViewModel;
        Transactions = transactionsViewModel;
        Chat = chatViewModel;
        Alerts = alertsViewModel;
        Advisor = goalsViewModel;
        Settings = settingsViewModel;
        _loginService = loginService;
        _logger = logger;
        AppVersion = ResolveAppVersion();

        CurrentPage = Dashboard;
        Dashboard.LoadCommand.Execute(null);
    }

    public string AppVersion { get; }

    public DashboardViewModel Dashboard { get; }

    public ImportViewModel Import { get; }

    public TransactionsViewModel Transactions { get; }

    public ChatViewModel Chat { get; }

    public AlertsViewModel Alerts { get; }

    public GoalsViewModel Advisor { get; }

    public SettingsViewModel Settings { get; }

    public bool IsDashboardActive => ReferenceEquals(CurrentPage, Dashboard);

    public bool IsImportActive => ReferenceEquals(CurrentPage, Import);

    public bool IsTransactionsActive => ReferenceEquals(CurrentPage, Transactions);

    public bool IsChatActive => ReferenceEquals(CurrentPage, Chat);

    public bool IsAlertsActive => ReferenceEquals(CurrentPage, Alerts);

    public bool IsAdvisorActive => ReferenceEquals(CurrentPage, Advisor);

    public bool IsSettingsActive => ReferenceEquals(CurrentPage, Settings);

    public event EventHandler? LoggedOut;

    [RelayCommand]
    private void ShowDashboard()
    {
        if (IsDashboardActive)
        {
            return;
        }

        CurrentPage = Dashboard;
        Dashboard.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private void ShowImport()
    {
        if (IsImportActive)
        {
            return;
        }

        CurrentPage = Import;
        Import.LoadAccountsCommand.Execute(null);
    }

    [RelayCommand]
    private void ShowTransactions()
    {
        if (IsTransactionsActive)
        {
            return;
        }

        CurrentPage = Transactions;
        Transactions.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private void ShowChat()
    {
        if (IsChatActive)
        {
            return;
        }

        CurrentPage = Chat;
    }

    [RelayCommand]
    private void ShowAlerts()
    {
        if (IsAlertsActive)
        {
            return;
        }

        CurrentPage = Alerts;
        Alerts.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private void ShowAdvisor()
    {
        if (IsAdvisorActive)
        {
            return;
        }

        CurrentPage = Advisor;
        Advisor.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private void ShowSettings()
    {
        if (IsSettingsActive)
        {
            return;
        }

        CurrentPage = Settings;
        Settings.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _loginService
                .LogoutAsync(CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // LogoutAsync inside LoginService is already defensive; if it still throws
            // we log here and still raise LoggedOut — leaving the user stuck on the
            // MainWindow is a worse outcome than navigating away despite the failure.
            _logger.LogError(ex, "Logout failed; still raising LoggedOut event");
        }
        finally
        {
            IsBusy = false;
            LoggedOut?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string ResolveAppVersion()
    {
        var entry = Assembly.GetEntryAssembly();
        var informational = entry?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }
        var version = entry?.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(version) ? "unknown" : version;
    }
}
