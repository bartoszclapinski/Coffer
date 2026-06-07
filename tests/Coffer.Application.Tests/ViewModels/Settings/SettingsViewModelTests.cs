using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Settings;
using Coffer.Core.Ai;
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

    private static SettingsViewModel Create(
        FakeAiSettings settings,
        FakeSecretStore secrets,
        FakeAiUsageLedger ledger) =>
        new(settings, secrets, ledger, NullLogger<SettingsViewModel>.Instance);
}
