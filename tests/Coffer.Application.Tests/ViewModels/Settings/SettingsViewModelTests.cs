using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Settings;
using Coffer.Core.Ai;
using Coffer.Core.Localization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Settings;

public class SettingsViewModelTests
{
    [Fact]
    public async Task Load_PopulatesFromSettingsLedgerAndSecretPresence()
    {
        var settings = new FakeAiSettings { MonthlyCapPln = 30m, ActiveProvider = AiDefaults.OpenAiProvider };
        var secrets = new FakeSecretStore();
        await secrets.SetSecretAsync(AiDefaults.ClaudeApiKeySecret, "sk-ant-x", CancellationToken.None);
        var ledger = new FakeAiUsageLedger { CurrentMonthSpendPln = 7.5m };
        var vm = Create(settings, secrets, ledger);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.MonthlyCapPln.Should().Be(30m);
        vm.SelectedProvider.Should().Be(AiDefaults.OpenAiProvider);
        vm.CurrentMonthSpendPln.Should().Be(7.5m);
        vm.HasApiKey.Should().BeTrue();
    }

    [Fact]
    public async Task Save_PersistsCapProviderAndModel()
    {
        var settings = new FakeAiSettings();
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger());

        vm.MonthlyCapPln = 42m;
        vm.SelectedProvider = AiDefaults.OpenAiProvider;
        vm.CategorizationModel = "gpt-4o-mini";

        await vm.SaveCommand.ExecuteAsync(null);

        settings.MonthlyCapPln.Should().Be(42m);
        settings.ActiveProvider.Should().Be(AiDefaults.OpenAiProvider);
        settings.CategorizationModel.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public async Task Load_PopulatesAiFallbackToggleAndOwnerName()
    {
        var settings = new FakeAiSettings { AiFallbackParsingEnabled = true, OwnerIdentityNames = "Jan Kowalski" };
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger());

        await vm.LoadCommand.ExecuteAsync(null);

        vm.AiFallbackParsingEnabled.Should().BeTrue();
        vm.OwnerIdentityNames.Should().Be("Jan Kowalski");
    }

    [Fact]
    public async Task Load_NullOwnerName_BindsToEmptyString()
    {
        var settings = new FakeAiSettings { OwnerIdentityNames = null };
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger());

        await vm.LoadCommand.ExecuteAsync(null);

        vm.OwnerIdentityNames.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_PersistsAiFallbackToggleAndOwnerName()
    {
        var settings = new FakeAiSettings();
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger());

        vm.AiFallbackParsingEnabled = true;
        vm.OwnerIdentityNames = "Jan Kowalski, J. Kowalski";

        await vm.SaveCommand.ExecuteAsync(null);

        settings.AiFallbackParsingEnabled.Should().BeTrue();
        settings.OwnerIdentityNames.Should().Be("Jan Kowalski, J. Kowalski");
    }

    [Fact]
    public async Task SaveApiKey_StoresSecretAndClearsInput()
    {
        var secrets = new FakeSecretStore();
        var vm = Create(new FakeAiSettings(), secrets, new FakeAiUsageLedger());

        vm.ApiKeyInput = "  sk-ant-trimmed  ";
        await vm.SaveApiKeyCommand.ExecuteAsync(null);

        (await secrets.GetSecretAsync(AiDefaults.ClaudeApiKeySecret, CancellationToken.None))
            .Should().Be("sk-ant-trimmed");
        vm.ApiKeyInput.Should().BeEmpty();
        vm.HasApiKey.Should().BeTrue();
    }

    [Fact]
    public void SaveApiKey_CannotExecute_WhenInputBlank()
    {
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger());

        vm.ApiKeyInput = "   ";

        vm.SaveApiKeyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task ClearApiKey_DeletesSecret()
    {
        var secrets = new FakeSecretStore();
        await secrets.SetSecretAsync(AiDefaults.ClaudeApiKeySecret, "sk-ant-x", CancellationToken.None);
        var vm = Create(new FakeAiSettings(), secrets, new FakeAiUsageLedger());
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.ClearApiKeyCommand.ExecuteAsync(null);

        (await secrets.GetSecretAsync(AiDefaults.ClaudeApiKeySecret, CancellationToken.None)).Should().BeNull();
        vm.HasApiKey.Should().BeFalse();
    }

    [Fact]
    public void SelectingLanguage_SwitchesLocalizerAndPersists()
    {
        var localizer = new FakeLocalizer();
        var store = new FakeLanguageStore();
        var vm = new SettingsViewModel(
            new FakeAiSettings(),
            new FakePlanningSettings(),
            new FakeAccountService(),
            new FakeSecretStore(),
            new FakeAiUsageLedger(),
            new FakeBackupService(),
            new FakeArchiveExporter(),
            new FakeFilePicker(),
            localizer,
            store,
            NullLogger<SettingsViewModel>.Instance);

        vm.SelectedLanguage = vm.Languages.First(l => l.Language == AppLanguage.English);

        localizer.Current.Should().Be(AppLanguage.English);
        store.Stored.Should().Be(AppLanguage.English);
        store.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Load_PopulatesSafetyFloorAndAccounts()
    {
        var planning = new FakePlanningSettings { SafetyFloor = 1500m };
        var accounts = new FakeAccountService();
        var accountId = Guid.NewGuid();
        accounts.SeedAnchor(accountId, "PKO", "PKO_BP", new DateOnly(2026, 1, 1), 4210.55m);
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), planning, accounts);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.SafetyFloorPln.Should().Be(1500m);
        vm.HasAccounts.Should().BeTrue();
        var row = vm.Accounts.Single();
        row.HasAnchor.Should().BeTrue();
        row.AnchorBalance.Should().Be(4210.55m);
        row.AnchorDate!.Value.Date.Should().Be(new DateTime(2026, 1, 1));
    }

    [Fact]
    public async Task Save_PersistsSafetyFloor()
    {
        var planning = new FakePlanningSettings();
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), planning, new FakeAccountService());

        vm.SafetyFloorPln = 2000m;
        await vm.SaveCommand.ExecuteAsync(null);

        planning.SafetyFloor.Should().Be(2000m);
    }

    [Fact]
    public async Task SaveAnchor_PersistsBalanceAndDate()
    {
        var accounts = new FakeAccountService();
        var accountId = Guid.NewGuid();
        accounts.SeedAnchor(accountId, "PKO", "PKO_BP", date: null, balance: null);
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), new FakePlanningSettings(), accounts);
        await vm.LoadCommand.ExecuteAsync(null);

        var row = vm.Accounts.Single();
        row.AnchorBalance = 3000m;
        row.AnchorDate = new DateTimeOffset(new DateTime(2026, 1, 1), TimeSpan.Zero);
        await row.SaveAnchorCommand.ExecuteAsync(null);

        accounts.LastAnchor.Should().Be((accountId, (decimal?)3000m, (DateOnly?)new DateOnly(2026, 1, 1)));
        row.HasAnchor.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAnchor_WithFutureDate_DoesNotPersist()
    {
        var accounts = new FakeAccountService();
        var accountId = Guid.NewGuid();
        accounts.SeedAnchor(accountId, "PKO", "PKO_BP", date: null, balance: null);
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), new FakePlanningSettings(), accounts);
        await vm.LoadCommand.ExecuteAsync(null);

        var row = vm.Accounts.Single();
        row.AnchorBalance = 3000m;
        row.AnchorDate = DateTimeOffset.Now.AddDays(3);
        await row.SaveAnchorCommand.ExecuteAsync(null);

        accounts.SetAnchorCalls.Should().Be(0, "a future anchor date is rejected before persistence");
    }

    [Fact]
    public async Task ClearAnchor_RemovesAnchor()
    {
        var accounts = new FakeAccountService();
        var accountId = Guid.NewGuid();
        accounts.SeedAnchor(accountId, "PKO", "PKO_BP", new DateOnly(2026, 1, 1), 4210.55m);
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), new FakePlanningSettings(), accounts);
        await vm.LoadCommand.ExecuteAsync(null);

        var row = vm.Accounts.Single();
        await row.ClearAnchorCommand.ExecuteAsync(null);

        accounts.LastAnchor.Should().Be((accountId, (decimal?)null, (DateOnly?)null));
        row.HasAnchor.Should().BeFalse();
        row.AnchorDate.Should().BeNull();
    }

    private static SettingsViewModel Create(
        FakeAiSettings settings,
        FakeSecretStore secrets,
        FakeAiUsageLedger ledger) =>
        Create(settings, secrets, ledger, new FakePlanningSettings(), new FakeAccountService());

    private static SettingsViewModel Create(
        FakeAiSettings settings,
        FakeSecretStore secrets,
        FakeAiUsageLedger ledger,
        FakePlanningSettings planning,
        FakeAccountService accounts) =>
        new(settings, planning, accounts, secrets, ledger,
            new FakeBackupService(), new FakeArchiveExporter(), new FakeFilePicker(),
            new FakeLocalizer(), new FakeLanguageStore(), NullLogger<SettingsViewModel>.Instance);

    private static SettingsViewModel CreateWithBackup(
        FakeBackupService backup,
        FakeArchiveExporter exporter,
        FakeFilePicker picker) =>
        new(new FakeAiSettings(), new FakePlanningSettings(), new FakeAccountService(),
            new FakeSecretStore(), new FakeAiUsageLedger(), backup, exporter, picker,
            new FakeLocalizer(), new FakeLanguageStore(), NullLogger<SettingsViewModel>.Instance);

    [Fact]
    public async Task Load_PopulatesBackupStatus()
    {
        var backup = new FakeBackupService { Status = new(new DateOnly(2026, 7, 3), 4, null) };
        var vm = CreateWithBackup(backup, new FakeArchiveExporter(), new FakeFilePicker());

        await vm.LoadCommand.ExecuteAsync(null);

        vm.LastDailySnapshotText.Should().Be("2026-07-03");
        vm.DailyBackupCountText.Should().Be("4");
    }

    [Fact]
    public async Task BackupNow_CreatesSnapshotAndRefreshesStatus()
    {
        var backup = new FakeBackupService();
        var vm = CreateWithBackup(backup, new FakeArchiveExporter(), new FakeFilePicker());

        await vm.BackupNowCommand.ExecuteAsync(null);

        backup.NowCalls.Should().Be(1);
    }

    [Fact]
    public async Task ExportArchive_WithChosenPath_ExportsThere()
    {
        var exporter = new FakeArchiveExporter();
        var picker = new FakeFilePicker { SaveResult = @"C:\backups\archive.zip" };
        var vm = CreateWithBackup(new FakeBackupService(), exporter, picker);

        await vm.ExportArchiveCommand.ExecuteAsync(null);

        picker.SaveCalls.Should().Be(1);
        exporter.Calls.Should().Be(1);
        exporter.LastTarget.Should().Be(@"C:\backups\archive.zip");
    }

    [Fact]
    public async Task ExportArchive_WhenCancelled_DoesNotExport()
    {
        var exporter = new FakeArchiveExporter();
        var picker = new FakeFilePicker { SaveResult = null }; // cancelled
        var vm = CreateWithBackup(new FakeBackupService(), exporter, picker);

        await vm.ExportArchiveCommand.ExecuteAsync(null);

        picker.SaveCalls.Should().Be(1);
        exporter.Calls.Should().Be(0);
    }
}
