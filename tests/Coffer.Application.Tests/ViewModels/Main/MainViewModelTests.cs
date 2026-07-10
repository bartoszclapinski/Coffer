using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Alerts;
using Coffer.Application.ViewModels.Budgets;
using Coffer.Application.ViewModels.Chat;
using Coffer.Application.ViewModels.Dashboard;
using Coffer.Application.ViewModels.Forecast;
using Coffer.Application.ViewModels.Goals;
using Coffer.Application.ViewModels.Import;
using Coffer.Application.ViewModels.Main;
using Coffer.Application.ViewModels.Planning;
using Coffer.Application.ViewModels.Settings;
using Coffer.Application.ViewModels.Spending;
using Coffer.Application.ViewModels.Transactions;
using Coffer.Core.Anomalies;
using Coffer.Core.Chat;
using Coffer.Core.Planning;
using Coffer.Core.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Main;

public class MainViewModelTests
{
    [Fact]
    public async Task LogoutCommand_CallsLoginServiceLogoutAndRaisesLoggedOut()
    {
        var service = new RecordingLoginService();
        var vm = CreateViewModel(service);

        var raised = 0;
        vm.LoggedOut += (_, _) => raised++;

        await vm.LogoutCommand.ExecuteAsync(null);

        service.LogoutCalls.Should().Be(1);
        raised.Should().Be(1);
    }

    [Fact]
    public async Task LogoutCommand_WhenServiceThrows_StillRaisesLoggedOut()
    {
        var service = new RecordingLoginService
        {
            ThrowOnLogout = new InvalidOperationException("test"),
        };
        var vm = CreateViewModel(service);

        var raised = 0;
        vm.LoggedOut += (_, _) => raised++;

        await vm.LogoutCommand.ExecuteAsync(null);

        raised.Should().Be(1,
            "leaving the user stuck on MainWindow because LogoutAsync failed is worse than navigating anyway");
    }

    [Fact]
    public void AppVersion_ReturnsNonEmptyString()
    {
        var vm = CreateViewModel(new RecordingLoginService());

        vm.AppVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void StartsOnDashboard_WithFirstNavItemActive()
    {
        var vm = CreateViewModel(new RecordingLoginService());

        vm.CurrentPage.Should().BeSameAs(vm.Dashboard);
        vm.NavItems[0].Key.Should().Be("dashboard");
        vm.NavItems[0].IsActive.Should().BeTrue();
        vm.ActiveTitle.Should().Be(vm.NavItems[0].Title);
    }

    [Fact]
    public void NavItems_CoverEverySectionPlusSettings()
    {
        var vm = CreateViewModel(new RecordingLoginService());

        var keys = vm.NavItems.Select(n => n.Key).ToArray();
        keys.Should().BeEquivalentTo(new[]
        {
            "dashboard", "transactions", "spending", "budgets", "forecast", "advisor",
            "planning", "affordability", "import", "alerts", "chat",
        });
        vm.SettingsItem.Key.Should().Be("settings");
    }

    [Fact]
    public void Navigate_SwitchesActivePageTitleAndFlags()
    {
        var vm = CreateViewModel(new RecordingLoginService());
        var target = vm.NavItems.Single(n => n.Key == "transactions");

        vm.NavigateCommand.Execute(target);

        vm.CurrentPage.Should().BeSameAs(vm.Transactions);
        vm.ActiveTitle.Should().Be(target.Title);
        target.IsActive.Should().BeTrue();
        vm.NavItems.Single(n => n.Key == "dashboard").IsActive.Should().BeFalse();
    }

    [Fact]
    public void Navigate_ToSettingsItem_ActivatesSettings()
    {
        var vm = CreateViewModel(new RecordingLoginService());

        vm.NavigateCommand.Execute(vm.SettingsItem);

        vm.CurrentPage.Should().BeSameAs(vm.Settings);
        vm.SettingsItem.IsActive.Should().BeTrue();
    }

    [Fact]
    public void ToggleBalances_FlipsHideBalances()
    {
        var vm = CreateViewModel(new RecordingLoginService());
        vm.HideBalances.Should().BeFalse();

        vm.ToggleBalancesCommand.Execute(null);

        vm.HideBalances.Should().BeTrue();
    }

    [Fact]
    public void ToggleTheme_InvokesSwitcherAndUpdatesIsDarkTheme()
    {
        var switcher = new FakeThemeSwitcher();
        var vm = CreateViewModel(new RecordingLoginService(), switcher);
        vm.IsDarkTheme.Should().BeFalse();

        vm.ToggleThemeCommand.Execute(null);

        switcher.ToggleCalls.Should().Be(1);
        vm.IsDarkTheme.Should().BeTrue();
    }

    [Fact]
    public void OpenCommandPalette_OpensWithNavAndActionCommands()
    {
        var vm = CreateViewModel(new RecordingLoginService());

        vm.OpenCommandPaletteCommand.Execute(null);

        vm.Palette.IsOpen.Should().BeTrue();
        // 11 rail items + Settings + Switch-theme + Show/hide-balances.
        vm.Palette.Results.Should().HaveCount(vm.NavItems.Count + 3);
    }

    [Fact]
    public void PaletteNavigationCommand_SwitchesPage()
    {
        var vm = CreateViewModel(new RecordingLoginService());
        vm.OpenCommandPaletteCommand.Execute(null);

        // Nav commands come first, in NavItems order (fake localizer echoes keys, so
        // resolve the budgets command by index rather than by its display title).
        var budgetsIndex = vm.NavItems.ToList().FindIndex(n => n.Key == "budgets");
        vm.Palette.SelectedIndex = budgetsIndex;
        vm.Palette.ExecuteSelected();

        vm.CurrentPage.Should().BeSameAs(vm.Budgets);
        vm.Palette.IsOpen.Should().BeFalse();
    }

    private static MainViewModel CreateViewModel(ILoginService loginService, FakeThemeSwitcher? themeSwitcher = null)
    {
        var dashboard = new DashboardViewModel(
            new FakeDashboardQuery(),
            new FakeLocalizer(),
            NullLogger<DashboardViewModel>.Instance);
        var import = new ImportViewModel(
            new FakeFilePicker(),
            new FakeImportStatementUseCase(),
            new FakeAccountService(),
            new FakeLocalizer(),
            NullLogger<ImportViewModel>.Instance);
        var transactions = new TransactionsViewModel(
            new FakeGetTransactionsQuery(),
            new FakeCategoryService(),
            new FakeLocalizer(),
            NullLogger<TransactionsViewModel>.Instance);
        var chat = new ChatViewModel(
            new StubChatService(),
            new FakeLocalizer(),
            NullLogger<ChatViewModel>.Instance);
        var alerts = new AlertsViewModel(
            new FakeDetectAnomaliesUseCase(),
            new FakeAlertsQuery(),
            new FakeAlertService(),
            new FakeLocalizer(),
            NullLogger<AlertsViewModel>.Instance);
        var advisor = new GoalsViewModel(
            new FakeGoalsQuery(),
            new FakeGoalService([]),
            new FakeFinancialContextBuilder(),
            new FakeGoalFeasibilityEngine(),
            new FakeAdvisorReportQuery(),
            new FakeLocalizer(),
            NullLogger<GoalsViewModel>.Instance);
        var settings = new SettingsViewModel(
            new FakeAiSettings(),
            new FakePlanningSettings(),
            new FakeAccountService(),
            new FakeSecretStore(),
            new FakeAiUsageLedger(),
            new FakeBackupService(),
            new FakeArchiveExporter(),
            new FakeFilePicker(),
            new FakeRestoreDialogService(),
            new FakeSeedRecoveryService(),
            new FakeEnableSeedRecoveryDialog(),
            new FakeChangeMasterPasswordDialog(),
            new FakeLocalizer(),
            new FakeLanguageStore(),
            NullLogger<SettingsViewModel>.Instance);
        var planning = new CashFlowPlanningViewModel(
            new FakeRecurringFlowRepository(),
            new FakeRecurringFlowDetector(),
            new FakeRunningBalanceQuery(),
            new FakeStatementContinuityChecker(),
            new FakePlanningSettings(),
            new CashFlowProjectionEngine(),
            new FakeCashFlowExplainer(),
            new FakeLocalizer(),
            NullLogger<CashFlowPlanningViewModel>.Instance);
        var affordability = new AffordabilityViewModel(
            new AffordabilityEngine(new CashFlowProjectionEngine()),
            new FakeRunningBalanceQuery(),
            new FakeVariableBurnQuery(),
            new FakeBalanceTrustQuery(),
            new FakePlanningSettings(),
            new FakeRecurringFlowRepository(),
            new FakeAccountService(),
            new FakeLocalizer(),
            NullLogger<AffordabilityViewModel>.Instance);
        var spending = new SpendingExplorerViewModel(
            new FakeSpendingExplorerQuery(),
            new FakeAccountService(),
            new FakeLocalizer(),
            NullLogger<SpendingExplorerViewModel>.Instance);
        var budgets = new BudgetsViewModel(
            new FakeCategoryBudgetRepository(),
            new FakeBudgetTrackingQuery(),
            new FakeCategoryService(),
            new FakeLocalizer(),
            NullLogger<BudgetsViewModel>.Instance);
        var forecast = new ForecastViewModel(
            new FakeExpenseForecastQuery(),
            new FakeCategoryBudgetRepository(),
            new FakeLocalizer(),
            NullLogger<ForecastViewModel>.Instance);

        return new MainViewModel(
            dashboard, import, transactions, chat, alerts, advisor, planning, affordability, spending, budgets, forecast, settings,
            loginService, new FakeLocalizer(), themeSwitcher ?? new FakeThemeSwitcher(), NullLogger<MainViewModel>.Instance);
    }

    private sealed class StubChatService : IChatService
    {
        public Task<ChatTurn> AskAsync(string question, IReadOnlyList<ChatMessage> history, CancellationToken ct) =>
            Task.FromResult(new ChatTurn("", []));
    }

    private sealed class RecordingLoginService : ILoginService
    {
        public int LogoutCalls { get; private set; }

        public Exception? ThrowOnLogout { get; set; }

        public Task<bool> TryLoginFromCachedKeyAsync(CancellationToken ct) =>
            Task.FromResult(false);

        public Task LoginWithPasswordAsync(string masterPassword, CancellationToken ct) =>
            Task.CompletedTask;

        public Task LogoutAsync(CancellationToken ct)
        {
            LogoutCalls++;
            return ThrowOnLogout is null
                ? Task.CompletedTask
                : Task.FromException(ThrowOnLogout);
        }
    }
}
