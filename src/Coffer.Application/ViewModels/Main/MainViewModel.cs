using System.Reflection;
using Coffer.Application.Localization;
using Coffer.Application.Theming;
using Coffer.Application.ViewModels.Alerts;
using Coffer.Application.ViewModels.Budgets;
using Coffer.Application.ViewModels.Chat;
using Coffer.Application.ViewModels.Dashboard;
using Coffer.Application.ViewModels.Forecast;
using Coffer.Application.ViewModels.Goals;
using Coffer.Application.ViewModels.Import;
using Coffer.Application.ViewModels.Planning;
using Coffer.Application.ViewModels.Settings;
using Coffer.Application.ViewModels.Shell;
using Coffer.Application.ViewModels.Spending;
using Coffer.Application.ViewModels.Transactions;
using Coffer.Core.Security;
using Coffer.Core.Theming;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Main;

/// <summary>
/// Shell view-model behind the post-login <c>MainWindow</c> (the "pro terminal" chrome).
/// Owns the data-driven <see cref="NavItems"/> model that powers both the icon rail and the
/// <see cref="Palette"/>, swaps <see cref="CurrentPage"/> via a single <see cref="NavigateCommand"/>,
/// and holds the app-wide theme toggle + balance-privacy state. Still owns the logout command
/// and <see cref="LoggedOut"/> event that <c>App.axaml.cs</c> subscribes to.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ILoginService _loginService;
    private readonly ILocalizer _localizer;
    private readonly IThemeSwitcher _themeSwitcher;
    private readonly ILogger<MainViewModel> _logger;
    private readonly List<NavItem> _allNav;
    private NavItem? _activeItem;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private ObservableObject? _currentPage;

    [ObservableProperty]
    private bool _hideBalances;

    public MainViewModel(
        DashboardViewModel dashboardViewModel,
        ImportViewModel importViewModel,
        TransactionsViewModel transactionsViewModel,
        ChatViewModel chatViewModel,
        AlertsViewModel alertsViewModel,
        GoalsViewModel goalsViewModel,
        CashFlowPlanningViewModel planningViewModel,
        AffordabilityViewModel affordabilityViewModel,
        SpendingExplorerViewModel spendingViewModel,
        BudgetsViewModel budgetsViewModel,
        ForecastViewModel forecastViewModel,
        SettingsViewModel settingsViewModel,
        ILoginService loginService,
        ILocalizer localizer,
        IThemeSwitcher themeSwitcher,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(dashboardViewModel);
        ArgumentNullException.ThrowIfNull(importViewModel);
        ArgumentNullException.ThrowIfNull(transactionsViewModel);
        ArgumentNullException.ThrowIfNull(chatViewModel);
        ArgumentNullException.ThrowIfNull(alertsViewModel);
        ArgumentNullException.ThrowIfNull(goalsViewModel);
        ArgumentNullException.ThrowIfNull(planningViewModel);
        ArgumentNullException.ThrowIfNull(affordabilityViewModel);
        ArgumentNullException.ThrowIfNull(spendingViewModel);
        ArgumentNullException.ThrowIfNull(budgetsViewModel);
        ArgumentNullException.ThrowIfNull(forecastViewModel);
        ArgumentNullException.ThrowIfNull(settingsViewModel);
        ArgumentNullException.ThrowIfNull(loginService);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(themeSwitcher);
        ArgumentNullException.ThrowIfNull(logger);

        Dashboard = dashboardViewModel;
        Import = importViewModel;
        Transactions = transactionsViewModel;
        Chat = chatViewModel;
        Alerts = alertsViewModel;
        Advisor = goalsViewModel;
        Planning = planningViewModel;
        Affordability = affordabilityViewModel;
        Spending = spendingViewModel;
        Budgets = budgetsViewModel;
        Forecast = forecastViewModel;
        Settings = settingsViewModel;
        _loginService = loginService;
        _localizer = localizer;
        _themeSwitcher = themeSwitcher;
        _logger = logger;
        AppVersion = ResolveAppVersion();

        // The single navigation model — rail order, with Settings pinned to the rail bottom.
        NavItems =
        [
            Item("dashboard", "Nav.Dashboard", "squares-four", Dashboard, () => Dashboard.LoadCommand.Execute(null)),
            Item("transactions", "Nav.Transactions", "arrows-left-right", Transactions, () => Transactions.LoadCommand.Execute(null)),
            Item("spending", "Nav.Spending", "chart-pie-slice", Spending, () => Spending.LoadCommand.Execute(null)),
            Item("budgets", "Nav.Budgets", "chart-pie", Budgets, () => Budgets.LoadCommand.Execute(null)),
            Item("forecast", "Nav.Forecast", "chart-line-up", Forecast, () => Forecast.LoadCommand.Execute(null)),
            Item("advisor", "Nav.Advisor", "target", Advisor, () => Advisor.LoadCommand.Execute(null)),
            Item("planning", "Nav.CashFlow", "calendar-blank", Planning, () => Planning.LoadCommand.Execute(null)),
            Item("affordability", "Nav.Affordability", "scales", Affordability, () => Affordability.LoadCommand.Execute(null)),
            Item("import", "Nav.Import", "file-arrow-down", Import, () => Import.LoadAccountsCommand.Execute(null)),
            Item("alerts", "Nav.Alerts", "bell", Alerts, () => Alerts.LoadCommand.Execute(null)),
            Item("chat", "Nav.Assistant", "chat-circle-dots", Chat, static () => { }),
        ];
        SettingsItem = Item("settings", "Nav.Settings", "gear", Settings, () => Settings.LoadCommand.Execute(null));

        _allNav = [.. NavItems, SettingsItem];

        _localizer.LanguageChanged += OnLanguageChanged;
        _themeSwitcher.Changed += OnThemeChanged;

        Navigate(NavItems[0]);
    }

    public DashboardViewModel Dashboard { get; }

    public ImportViewModel Import { get; }

    public TransactionsViewModel Transactions { get; }

    public ChatViewModel Chat { get; }

    public AlertsViewModel Alerts { get; }

    public GoalsViewModel Advisor { get; }

    public CashFlowPlanningViewModel Planning { get; }

    public AffordabilityViewModel Affordability { get; }

    public SpendingExplorerViewModel Spending { get; }

    public BudgetsViewModel Budgets { get; }

    public ForecastViewModel Forecast { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>Rail items (Settings excluded — it is pinned to the rail bottom via <see cref="SettingsItem"/>).</summary>
    public IReadOnlyList<NavItem> NavItems { get; }

    public NavItem SettingsItem { get; }

    public CommandPaletteViewModel Palette { get; } = new();

    public string AppVersion { get; }

    public string VersionText => _localizer.Format("Nav.Version", AppVersion);

    /// <summary>Title of the active screen, shown in the top bar (re-resolved on language change).</summary>
    public string ActiveTitle => _activeItem?.Title ?? "";

    public bool IsDarkTheme => _themeSwitcher.Current == AppTheme.Dark;

    public event EventHandler? LoggedOut;

    [RelayCommand]
    private void Navigate(NavItem? item)
    {
        if (item is null || ReferenceEquals(CurrentPage, item.Page))
        {
            return;
        }

        CurrentPage = item.Page;
        _activeItem = item;
        foreach (var nav in _allNav)
        {
            nav.IsActive = ReferenceEquals(nav, item);
        }

        OnPropertyChanged(nameof(ActiveTitle));
        item.Load();
    }

    /// <summary>Navigates by <see cref="NavItem.Key"/> (used by the command palette).</summary>
    public void NavigateToKey(string key)
    {
        var item = _allNav.FirstOrDefault(n => n.Key == key);
        if (item is not null)
        {
            Navigate(item);
        }
    }

    [RelayCommand]
    private void ToggleBalances() => HideBalances = !HideBalances;

    [RelayCommand]
    private void ToggleTheme() => _themeSwitcher.Toggle();

    [RelayCommand]
    private void OpenCommandPalette() => Palette.Open(BuildPaletteCommands());

    private IReadOnlyList<PaletteCommand> BuildPaletteCommands()
    {
        var commands = new List<PaletteCommand>(_allNav.Count + 2);
        foreach (var nav in _allNav)
        {
            var target = nav;
            commands.Add(new PaletteCommand(
                _localizer.Format("Palette.GoTo", nav.Title),
                _localizer["Palette.CatNavigate"],
                nav.Icon,
                () => Navigate(target)));
        }

        commands.Add(new PaletteCommand(
            _localizer["Palette.SwitchTheme"], _localizer["Palette.CatSetting"], "sun", ToggleTheme));
        commands.Add(new PaletteCommand(
            _localizer["Palette.ToggleBalances"], _localizer["Palette.CatSetting"], "eye", ToggleBalances));
        return commands;
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

    private NavItem Item(string key, string titleKey, string icon, ObservableObject page, Action load) =>
        new(key, titleKey, icon, page, load, _localizer);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var nav in _allNav)
        {
            nav.RefreshTitle();
        }

        OnPropertyChanged(nameof(ActiveTitle));
        OnPropertyChanged(nameof(VersionText));
    }

    private void OnThemeChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(IsDarkTheme));

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
