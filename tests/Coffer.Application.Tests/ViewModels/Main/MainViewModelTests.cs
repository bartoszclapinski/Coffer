using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Alerts;
using Coffer.Application.ViewModels.Chat;
using Coffer.Application.ViewModels.Dashboard;
using Coffer.Application.ViewModels.Goals;
using Coffer.Application.ViewModels.Import;
using Coffer.Application.ViewModels.Main;
using Coffer.Application.ViewModels.Settings;
using Coffer.Application.ViewModels.Transactions;
using Coffer.Core.Anomalies;
using Coffer.Core.Chat;
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
    public void StartsOnDashboardPage()
    {
        var vm = CreateViewModel(new RecordingLoginService());

        vm.IsDashboardActive.Should().BeTrue();
        vm.CurrentPage.Should().BeSameAs(vm.Dashboard);
    }

    [Fact]
    public void ShowImport_SwitchesActivePage()
    {
        var vm = CreateViewModel(new RecordingLoginService());

        vm.ShowImportCommand.Execute(null);

        vm.IsImportActive.Should().BeTrue();
        vm.CurrentPage.Should().BeSameAs(vm.Import);
    }

    [Fact]
    public void ShowTransactions_SwitchesActivePage()
    {
        var vm = CreateViewModel(new RecordingLoginService());

        vm.ShowTransactionsCommand.Execute(null);

        vm.IsTransactionsActive.Should().BeTrue();
        vm.CurrentPage.Should().BeSameAs(vm.Transactions);
    }

    [Fact]
    public void ShowChat_SwitchesActivePage()
    {
        var vm = CreateViewModel(new RecordingLoginService());

        vm.ShowChatCommand.Execute(null);

        vm.IsChatActive.Should().BeTrue();
        vm.CurrentPage.Should().BeSameAs(vm.Chat);
    }

    [Fact]
    public void ShowAlerts_SwitchesActivePage()
    {
        var vm = CreateViewModel(new RecordingLoginService());

        vm.ShowAlertsCommand.Execute(null);

        vm.IsAlertsActive.Should().BeTrue();
        vm.CurrentPage.Should().BeSameAs(vm.Alerts);
    }

    [Fact]
    public void ShowAdvisor_SwitchesActivePage()
    {
        var vm = CreateViewModel(new RecordingLoginService());

        vm.ShowAdvisorCommand.Execute(null);

        vm.IsAdvisorActive.Should().BeTrue();
        vm.CurrentPage.Should().BeSameAs(vm.Advisor);
    }

    private static MainViewModel CreateViewModel(ILoginService loginService)
    {
        var dashboard = new DashboardViewModel(
            new FakeDashboardQuery(),
            NullLogger<DashboardViewModel>.Instance);
        var import = new ImportViewModel(
            new FakeFilePicker(),
            new FakeImportStatementUseCase(),
            new FakeAccountService(),
            NullLogger<ImportViewModel>.Instance);
        var transactions = new TransactionsViewModel(
            new FakeGetTransactionsQuery(),
            new FakeCategoryService(),
            NullLogger<TransactionsViewModel>.Instance);
        var chat = new ChatViewModel(
            new StubChatService(),
            NullLogger<ChatViewModel>.Instance);
        var alerts = new AlertsViewModel(
            new FakeDetectAnomaliesUseCase(),
            new FakeAlertsQuery(),
            new FakeAlertService(),
            NullLogger<AlertsViewModel>.Instance);
        var advisor = new GoalsViewModel(
            new FakeGoalsQuery(),
            new FakeGoalService([]),
            new FakeFinancialContextBuilder(),
            new FakeGoalFeasibilityEngine(),
            new FakeAdvisorReportQuery(),
            NullLogger<GoalsViewModel>.Instance);
        var settings = new SettingsViewModel(
            new FakeAiSettings(),
            new FakeSecretStore(),
            new FakeAiUsageLedger(),
            new FakeLocalizer(),
            new FakeLanguageStore(),
            NullLogger<SettingsViewModel>.Instance);

        return new MainViewModel(
            dashboard, import, transactions, chat, alerts, advisor, settings, loginService, NullLogger<MainViewModel>.Instance);
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
