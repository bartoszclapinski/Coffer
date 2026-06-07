using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Import;
using Coffer.Application.ViewModels.Main;
using Coffer.Application.ViewModels.Settings;
using Coffer.Application.ViewModels.Transactions;
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
    public void StartsOnImportPage()
    {
        var vm = CreateViewModel(new RecordingLoginService());

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

    private static MainViewModel CreateViewModel(ILoginService loginService)
    {
        var import = new ImportViewModel(
            new FakeFilePicker(),
            new FakeImportStatementUseCase(),
            new FakeAccountService(),
            NullLogger<ImportViewModel>.Instance);
        var transactions = new TransactionsViewModel(
            new FakeGetTransactionsQuery(),
            new FakeCategoryService(),
            NullLogger<TransactionsViewModel>.Instance);
        var settings = new SettingsViewModel(
            new FakeAiSettings(),
            new FakeSecretStore(),
            new FakeAiUsageLedger(),
            NullLogger<SettingsViewModel>.Instance);

        return new MainViewModel(import, transactions, settings, loginService, NullLogger<MainViewModel>.Instance);
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
