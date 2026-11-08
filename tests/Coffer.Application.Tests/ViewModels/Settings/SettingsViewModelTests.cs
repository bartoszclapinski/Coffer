using System.Globalization;
using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Settings;
using Coffer.Core.Ai;
using Coffer.Core.Localization;
using Coffer.Core.Theming;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Settings;

public class SettingsViewModelTests
{
    private static readonly IReadOnlyList<OllamaModelInfo> _localModels =
    [
        new("qwen2.5:3b", ["completion", "tools"]),
        new("qwen3.5:latest", ["completion", "vision", "tools", "thinking"]),
    ];

    private static AiProviderOption Option(SettingsViewModel vm, string id) => vm.Providers.Single(p => p.Id == id);

    [Fact]
    public async Task Load_PopulatesFromSettingsLedgerAndSecretPresence()
    {
        var settings = new FakeAiSettings
        {
            MonthlyCapPln = 30m,
            ActiveProvider = AiDefaults.ClaudeProvider,
            CategorizationModel = "claude-sonnet-4-6",
        };
        var secrets = new FakeSecretStore();
        await secrets.SetSecretAsync(AiDefaults.ClaudeApiKeySecret, "sk-ant-x", CancellationToken.None);
        var ledger = new FakeAiUsageLedger { CurrentMonthSpendPln = 7.5m };
        var vm = Create(settings, secrets, ledger);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.MonthlyCapPln.Should().Be(30m);
        vm.Categorization.SelectedProvider.Id.Should().Be(AiDefaults.ClaudeProvider);
        vm.Categorization.SelectedModel.Should().Be("claude-sonnet-4-6");
        vm.CurrentMonthSpendPln.Should().Be(7.5m);
        vm.HasApiKey.Should().BeTrue();
        vm.ShowMissingApiKey.Should().BeFalse();
    }

    [Fact]
    public void Providers_OfferClaudeAndOllama_AndClaudeModelsAreTheFixedList()
    {
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger());

        vm.Providers.Select(p => p.Id).Should().Equal(AiDefaults.ClaudeProvider, AiDefaults.OllamaProvider);
        vm.Providers.Select(p => p.DisplayName)
            .Should().Equal("Settings.Ai.Provider.Claude", "Settings.Ai.Provider.Ollama");
        vm.Categorization.Models.Should().Equal("claude-haiku-5-5", "claude-sonnet-5-5", "claude-haiku-4-5", "claude-sonnet-4-6");
        vm.Analysis.Models.Should().Equal(AiDefaults.ClaudeModels);
        vm.Categorization.SelectedModel.Should().Be(AiDefaults.CategorizationModel);
        vm.Analysis.SelectedModel.Should().Be(AiDefaults.AnalysisModel);
    }

    [Fact]
    public async Task Load_BothRoles_ShowTheirStoredRoutes()
    {
        var settings = new FakeAiSettings
        {
            ActiveProvider = AiDefaults.ClaudeProvider,
            CategorizationModel = "claude-haiku-4-5",
            AnalysisProvider = AiDefaults.OllamaProvider,
            AnalysisModel = "qwen3.5:latest",
        };
        var catalog = new FakeOllamaCatalog { Models = _localModels };
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger(), catalog);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Categorization.IsClaude.Should().BeTrue();
        vm.Categorization.SelectedModel.Should().Be("claude-haiku-4-5");
        vm.Analysis.IsOllama.Should().BeTrue();
        vm.Analysis.SelectedModel.Should().Be("qwen3.5:latest");
        vm.Analysis.Models.Should().Equal("qwen2.5:3b", "qwen3.5:latest");
        vm.Analysis.CapabilitiesText.Should().Be("completion · vision · tools · thinking");
        vm.Analysis.ModelNotListed.Should().BeFalse();
        catalog.ListedEndpoints.Should().ContainSingle("a role on Ollama needs the server's model list");
    }

    [Fact]
    public async Task Load_AllRolesOnClaude_DoesNotContactOllama()
    {
        var catalog = new FakeOllamaCatalog { Models = _localModels };
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), catalog);

        await vm.LoadCommand.ExecuteAsync(null);

        catalog.ListedEndpoints.Should().BeEmpty();
        catalog.TestedEndpoints.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_StoredOllamaModelNotOnTheServer_IsKeptAndFlagged()
    {
        var settings = new FakeAiSettings { ActiveProvider = AiDefaults.OllamaProvider, CategorizationModel = "llama3.2:1b" };
        var catalog = new FakeOllamaCatalog { Models = _localModels };
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger(), catalog);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Categorization.SelectedModel.Should().Be("llama3.2:1b");
        vm.Categorization.Models.Should().Equal("qwen2.5:3b", "qwen3.5:latest", "llama3.2:1b");
        vm.Categorization.ModelNotListed.Should().BeTrue();
        vm.Categorization.IsEdited.Should().BeFalse();
    }

    [Fact]
    public async Task Load_OllamaDown_KeepsTheStoredModelAndShowsTheError()
    {
        var settings = new FakeAiSettings { AnalysisProvider = AiDefaults.OllamaProvider, AnalysisModel = "qwen3.5:latest" };
        var catalog = new FakeOllamaCatalog
        {
            ListFailure = new AiProviderUnavailableException(
                AiDefaults.OllamaProvider, AiDefaults.OllamaEndpoint, null, AiProviderUnavailableReason.Unreachable),
        };
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger(), catalog);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Analysis.SelectedModel.Should().Be("qwen3.5:latest");
        vm.Analysis.Models.Should().Equal("qwen3.5:latest");
        vm.Analysis.ModelNotListed.Should().BeFalse("nothing is known about the server's models");
        vm.OllamaModelsFailed.Should().BeTrue();
        vm.OllamaModelsStatusText.Should().Be("Settings.Ai.Ollama.Error.Unreachable");
        vm.StatusMessage.Should().BeEmpty("an unreachable Ollama is not a load failure");
    }

    [Fact]
    public async Task Load_StoredChoiceNoLongerOffered_ShowsTheDefaults()
    {
        var settings = new FakeAiSettings { ActiveProvider = "Retired", CategorizationModel = "retired-model" };
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger());

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Categorization.SelectedProvider.Id.Should().Be(AiDefaults.ClaudeProvider);
        vm.Categorization.SelectedModel.Should().Be(AiDefaults.CategorizationModel);
    }

    [Fact]
    public async Task Load_ShowsTheStoredEndpoint()
    {
        var settings = new FakeAiSettings { OllamaEndpoint = new Uri("http://192.168.1.20:11434/") };
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger());

        await vm.LoadCommand.ExecuteAsync(null);

        vm.OllamaEndpointText.Should().Be("http://192.168.1.20:11434");
        vm.IsEndpointRemote.Should().BeTrue();
    }

    [Fact]
    public async Task Save_PersistsBothRolesCapAndEndpoint()
    {
        var settings = new FakeAiSettings();
        var catalog = new FakeOllamaCatalog { Models = _localModels };
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger(), catalog);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.MonthlyCapPln = 42m;
        vm.Categorization.SelectedModel = "claude-haiku-4-5";
        vm.Analysis.SelectedProvider = Option(vm, AiDefaults.OllamaProvider);
        vm.Analysis.SelectedModel = "qwen3.5:latest";
        vm.OllamaEndpointText = " http://127.0.0.1:11434/ ";

        await vm.SaveCommand.ExecuteAsync(null);

        settings.MonthlyCapPln.Should().Be(42m);
        settings.ActiveProvider.Should().Be(AiDefaults.ClaudeProvider);
        settings.CategorizationModel.Should().Be("claude-haiku-4-5");
        settings.AnalysisProvider.Should().Be(AiDefaults.OllamaProvider);
        settings.AnalysisModel.Should().Be("qwen3.5:latest");
        settings.OllamaEndpoint.Should().Be(new Uri("http://127.0.0.1:11434"));
        vm.OllamaEndpointText.Should().Be("http://127.0.0.1:11434");
        settings.SetRouteCalls.Should().Be(2);
        vm.StatusMessage.Should().Be("Settings.Status.Saved");
        vm.Categorization.IsEdited.Should().BeFalse();
        vm.Analysis.IsEdited.Should().BeFalse();
    }

    [Fact]
    public async Task Save_AfterLoad_WithoutRouteChange_LeavesStoredRoutesUntouched()
    {
        // An unknown stored provider shows as Claude; saving another setting must not rewrite it.
        var settings = new FakeAiSettings
        {
            ActiveProvider = "SomethingNew",
            CategorizationModel = "x-model",
            AnalysisProvider = AiDefaults.OllamaProvider,
            AnalysisModel = "qwen2.5:3b",
        };
        var catalog = new FakeOllamaCatalog { Models = _localModels };
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger(), catalog);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.MonthlyCapPln = 50m;
        await vm.SaveCommand.ExecuteAsync(null);

        settings.MonthlyCapPln.Should().Be(50m);
        settings.ActiveProvider.Should().Be("SomethingNew");
        settings.CategorizationModel.Should().Be("x-model");
        settings.AnalysisProvider.Should().Be(AiDefaults.OllamaProvider);
        settings.AnalysisModel.Should().Be("qwen2.5:3b");
        settings.SetRouteCalls.Should().Be(0);
    }

    [Fact]
    public async Task Save_OnlyTheEditedRoleIsWritten()
    {
        var settings = new FakeAiSettings { ActiveProvider = AiDefaults.OllamaProvider, CategorizationModel = "qwen2.5:3b" };
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger(), new FakeOllamaCatalog { Models = _localModels });
        await vm.LoadCommand.ExecuteAsync(null);

        vm.Analysis.SelectedModel = "claude-haiku-5-5";
        await vm.SaveCommand.ExecuteAsync(null);

        settings.SetRouteCalls.Should().Be(1);
        settings.AnalysisModel.Should().Be("claude-haiku-5-5");
        settings.ActiveProvider.Should().Be(AiDefaults.OllamaProvider);
        settings.CategorizationModel.Should().Be("qwen2.5:3b");
    }

    [Fact]
    public async Task Save_OllamaRoleWithoutModel_IsRefusedAndNothingIsWritten()
    {
        var settings = new FakeAiSettings();
        var catalog = new FakeOllamaCatalog { Models = [] };
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger(), catalog);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.Categorization.SelectedProvider = Option(vm, AiDefaults.OllamaProvider);
        vm.MonthlyCapPln = 99m;
        await vm.SaveCommand.ExecuteAsync(null);

        vm.Categorization.SelectedModel.Should().BeNull();
        vm.StatusMessage.Should().Be("Settings.Ai.Model.Required");
        settings.SetRouteCalls.Should().Be(0);
        settings.MonthlyCapPln.Should().Be(AiDefaults.MonthlyCapPln);
    }

    [Fact]
    public async Task Save_InvalidEndpoint_IsRefusedAndNothingIsWritten()
    {
        var settings = new FakeAiSettings();
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger());
        await vm.LoadCommand.ExecuteAsync(null);

        vm.OllamaEndpointText = "ftp://user:pw@host";
        vm.MonthlyCapPln = 99m;
        await vm.SaveCommand.ExecuteAsync(null);

        vm.IsEndpointInvalid.Should().BeTrue();
        vm.StatusMessage.Should().Be("Settings.Ai.Ollama.EndpointInvalid");
        settings.OllamaEndpoint.Should().Be(OllamaEndpoint.Default);
        settings.MonthlyCapPln.Should().Be(AiDefaults.MonthlyCapPln);
    }

    [Fact]
    public async Task SwitchingProviderToOllama_ListsTheServerModels_AndPicksOne()
    {
        var catalog = new FakeOllamaCatalog { Models = _localModels };
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), catalog);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.Categorization.SelectedProvider = Option(vm, AiDefaults.OllamaProvider);

        catalog.ListedEndpoints.Should().ContainSingle().Which.Should().Be(OllamaEndpoint.Default);
        vm.Categorization.IsOllama.Should().BeTrue();
        vm.Categorization.Models.Should().Equal("qwen2.5:3b", "qwen3.5:latest");
        vm.Categorization.SelectedModel.Should().Be("qwen2.5:3b");
        vm.Categorization.IsEdited.Should().BeTrue();
        vm.Analysis.Models.Should().Equal(AiDefaults.ClaudeModels, "the other role is still on Claude");
        vm.OllamaModelsStatusText.Should().Be("Settings.Ai.Ollama.ModelsFound");
    }

    [Fact]
    public async Task SwitchingProviderBackToClaude_RestoresTheClaudeList()
    {
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(),
            new FakeOllamaCatalog { Models = _localModels });
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Analysis.SelectedModel = "claude-sonnet-4-6";

        vm.Analysis.SelectedProvider = Option(vm, AiDefaults.OllamaProvider);
        vm.Analysis.SelectedProvider = Option(vm, AiDefaults.ClaudeProvider);

        vm.Analysis.Models.Should().Equal(AiDefaults.ClaudeModels);
        vm.Analysis.SelectedModel.Should().Be("claude-sonnet-4-6", "the model last picked for Claude comes back");
    }

    [Fact]
    public async Task AnalysisOnOllama_PrefersAToolsCapableModel_AndFlagsOneWithoutTools()
    {
        var catalog = new FakeOllamaCatalog
        {
            Models = [new("gemma:2b", ["completion"]), new("qwen3.5:latest", ["completion", "tools"])],
        };
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), catalog);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.Analysis.SelectedProvider = Option(vm, AiDefaults.OllamaProvider);

        vm.Analysis.SelectedModel.Should().Be("qwen3.5:latest");
        vm.Analysis.ModelLacksTools.Should().BeFalse();

        vm.Analysis.SelectedModel = "gemma:2b";

        vm.Analysis.ModelLacksTools.Should().BeTrue("chat needs tool calling");
    }

    [Fact]
    public async Task UsesClaude_FollowsTheRoles_AndHidesTheMissingKeyWarning()
    {
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(),
            new FakeOllamaCatalog { Models = _localModels });
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ShowMissingApiKey.Should().BeTrue();

        vm.Categorization.SelectedProvider = Option(vm, AiDefaults.OllamaProvider);
        vm.UsesClaude.Should().BeTrue();

        vm.Analysis.SelectedProvider = Option(vm, AiDefaults.OllamaProvider);
        vm.UsesClaude.Should().BeFalse();
        vm.ShowMissingApiKey.Should().BeFalse("no role needs the key");
    }

    [Fact]
    public async Task RefreshModels_UsesTheTypedEndpoint_AndKeepsTheCurrentPick()
    {
        var settings = new FakeAiSettings { ActiveProvider = AiDefaults.OllamaProvider, CategorizationModel = "qwen3.5:latest" };
        var catalog = new FakeOllamaCatalog { Models = _localModels };
        var vm = Create(settings, new FakeSecretStore(), new FakeAiUsageLedger(), catalog);
        await vm.LoadCommand.ExecuteAsync(null);
        catalog.Models = [.. _localModels, new("llama3.2:3b", ["completion", "tools"])];

        vm.OllamaEndpointText = "http://localhost:11500";
        await vm.RefreshOllamaModelsCommand.ExecuteAsync(null);

        catalog.ListedEndpoints.Last().Should().Be(new Uri("http://localhost:11500"));
        vm.Categorization.Models.Should().Equal("qwen2.5:3b", "qwen3.5:latest", "llama3.2:3b");
        vm.Categorization.SelectedModel.Should().Be("qwen3.5:latest");
        vm.Categorization.IsEdited.Should().BeFalse("a refresh is not an edit");
        vm.OllamaModelsFailed.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshModels_EmptyServer_SaysSo()
    {
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), new FakeOllamaCatalog());

        await vm.RefreshOllamaModelsCommand.ExecuteAsync(null);

        vm.OllamaModelsFailed.Should().BeTrue();
        vm.OllamaModelsStatusText.Should().Be("Settings.Ai.Ollama.NoModels");
    }

    [Fact]
    public async Task RefreshModels_UnexpectedFailure_IsShownNotThrown()
    {
        var catalog = new FakeOllamaCatalog { ListFailure = new InvalidOperationException("boom") };
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), catalog);

        await vm.RefreshOllamaModelsCommand.ExecuteAsync(null);

        vm.OllamaModelsFailed.Should().BeTrue();
        vm.OllamaModelsStatusText.Should().Be("Settings.Ai.Ollama.Error.Rejected");
        vm.IsRefreshingModels.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnection_Success_ShowsTheVersion_AndListsModels()
    {
        var catalog = new FakeOllamaCatalog { Models = _localModels };
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), catalog);

        await vm.TestOllamaConnectionCommand.ExecuteAsync(null);

        catalog.TestedEndpoints.Should().ContainSingle().Which.Should().Be(OllamaEndpoint.Default);
        vm.ConnectionSucceeded.Should().BeTrue();
        vm.ConnectionFailed.Should().BeFalse();
        vm.ConnectionStatusText.Should().Be("Settings.Ai.Ollama.Connected");
        catalog.ListedEndpoints.Should().ContainSingle();
    }

    [Theory]
    [InlineData(AiProviderUnavailableReason.Unreachable, "Settings.Ai.Ollama.Error.Unreachable")]
    [InlineData(AiProviderUnavailableReason.Timeout, "Settings.Ai.Ollama.Error.Timeout")]
    [InlineData(AiProviderUnavailableReason.Rejected, "Settings.Ai.Ollama.Error.Rejected")]
    public async Task TestConnection_Failure_ShowsTheReason(AiProviderUnavailableReason reason, string expected)
    {
        var catalog = new FakeOllamaCatalog { Connection = OllamaConnectionResult.Failed(reason) };
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), catalog);

        await vm.TestOllamaConnectionCommand.ExecuteAsync(null);

        vm.ConnectionFailed.Should().BeTrue();
        vm.ConnectionSucceeded.Should().BeFalse();
        vm.ConnectionStatusText.Should().Be(expected);
        catalog.ListedEndpoints.Should().BeEmpty();
    }

    [Fact]
    public async Task TestConnection_InvalidEndpoint_DoesNotCallTheServer()
    {
        var catalog = new FakeOllamaCatalog();
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), catalog);

        vm.OllamaEndpointText = "not a url";
        await vm.TestOllamaConnectionCommand.ExecuteAsync(null);

        catalog.TestedEndpoints.Should().BeEmpty();
        vm.ConnectionFailed.Should().BeTrue();
        vm.ConnectionStatusText.Should().Be("Settings.Ai.Ollama.EndpointInvalid");
    }

    [Fact]
    public async Task EditingTheEndpoint_ClearsAnEarlierTestResult()
    {
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger(), new FakeOllamaCatalog());
        await vm.TestOllamaConnectionCommand.ExecuteAsync(null);
        vm.ConnectionSucceeded.Should().BeTrue();

        vm.OllamaEndpointText = "http://localhost:11435";

        vm.ConnectionSucceeded.Should().BeFalse();
        vm.ConnectionStatusText.Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://localhost:11434", false)]
    [InlineData("http://127.0.0.1:11434", false)]
    [InlineData("http://[::1]:11434", false)]
    [InlineData("http://192.168.1.20:11434", true)]
    [InlineData("https://ollama.example.com/proxy", true)]
    [InlineData("not a url", false)]
    public void LoopbackWarning_ShowsOnlyForAValidEndpointOffThisMachine(string text, bool remote)
    {
        var vm = Create(new FakeAiSettings(), new FakeSecretStore(), new FakeAiUsageLedger());

        vm.OllamaEndpointText = text;

        vm.IsEndpointRemote.Should().Be(remote);
    }

    [Fact]
    public void LanguageSwitch_RelabelsTheProviders()
    {
        var localizer = new RecordingLocalizer();
        var vm = new SettingsViewModel(
            new FakeAiSettings(), new FakeOllamaCatalog(), new FakePlanningSettings(), new FakeAccountService(),
            new FakeSecretStore(), new FakeAiUsageLedger(), new FakeBackupService(), new FakeArchiveExporter(),
            new FakeFilePicker(), new FakeRestoreDialogService(), new FakeSeedRecoveryService(),
            new FakeEnableSeedRecoveryDialog(), new FakeChangeMasterPasswordDialog(), localizer,
            new FakeLanguageStore(), new FakeThemeSwitcher(), NullLogger<SettingsViewModel>.Instance);
        vm.Providers[1].DisplayName.Should().Be("Polish:Settings.Ai.Provider.Ollama");

        localizer.SetLanguage(AppLanguage.English);

        vm.Providers[1].DisplayName.Should().Be("English:Settings.Ai.Provider.Ollama");
    }

    // Prefixes every string with the language, so a relabel is visible.
    private sealed class RecordingLocalizer : Coffer.Application.Localization.ILocalizer
    {
        public AppLanguage Current { get; private set; } = AppLanguage.Polish;

        public string this[string key] => $"{Current}:{key}";

        public string Format(string key, params object[] args) => this[key];

        public void SetLanguage(AppLanguage language)
        {
            Current = language;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? LanguageChanged;
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
            new FakeOllamaCatalog(),
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
            localizer,
            store,
            new FakeThemeSwitcher(),
            NullLogger<SettingsViewModel>.Instance);

        vm.SelectedLanguage = vm.Languages.First(l => l.Language == AppLanguage.English);

        localizer.Current.Should().Be(AppLanguage.English);
        store.Stored.Should().Be(AppLanguage.English);
        store.SaveCalls.Should().Be(1);
    }

    [Fact]
    public void SelectingDarkTheme_AppliesAndPersistsViaSwitcher()
    {
        var switcher = new FakeThemeSwitcher();
        var vm = CreateWithTheme(switcher);

        vm.IsDarkTheme.Should().BeFalse("the fake switcher starts on Light");

        vm.IsDarkTheme = true;

        switcher.Current.Should().Be(AppTheme.Dark);
        switcher.AppliedCount.Should().Be(1, "one user flip = one apply + save");
    }

    [Fact]
    public void Ctor_ReflectsCurrentTheme_WithoutReapplyingIt()
    {
        var switcher = new FakeThemeSwitcher(AppTheme.Dark);

        var vm = CreateWithTheme(switcher);

        vm.IsDarkTheme.Should().BeTrue();
        switcher.AppliedCount.Should().Be(0, "opening Settings must not rewrite theme.json");
    }

    [Fact]
    public void ExternalThemeChange_SyncsToggle_WithoutEchoingBack()
    {
        var switcher = new FakeThemeSwitcher();
        var vm = CreateWithTheme(switcher);

        // e.g. the top-bar sun/moon button or the command palette while Settings is open
        switcher.Toggle();

        vm.IsDarkTheme.Should().BeTrue();
        switcher.AppliedCount.Should().Be(1, "the toggle's echo back into the switcher is a no-op");
    }

    private static SettingsViewModel CreateWithTheme(FakeThemeSwitcher switcher) =>
        new(new FakeAiSettings(), new FakeOllamaCatalog(), new FakePlanningSettings(), new FakeAccountService(),
            new FakeSecretStore(), new FakeAiUsageLedger(), new FakeBackupService(),
            new FakeArchiveExporter(), new FakeFilePicker(), new FakeRestoreDialogService(),
            new FakeSeedRecoveryService(), new FakeEnableSeedRecoveryDialog(),
            new FakeChangeMasterPasswordDialog(), new FakeLocalizer(), new FakeLanguageStore(),
            switcher, NullLogger<SettingsViewModel>.Instance);

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
        FakeAiUsageLedger ledger,
        FakeOllamaCatalog? catalog = null) =>
        Create(settings, secrets, ledger, new FakePlanningSettings(), new FakeAccountService(), catalog);

    private static SettingsViewModel Create(
        FakeAiSettings settings,
        FakeSecretStore secrets,
        FakeAiUsageLedger ledger,
        FakePlanningSettings planning,
        FakeAccountService accounts,
        FakeOllamaCatalog? catalog = null) =>
        new(settings, catalog ?? new FakeOllamaCatalog(), planning, accounts, secrets, ledger,
            new FakeBackupService(), new FakeArchiveExporter(), new FakeFilePicker(),
            new FakeRestoreDialogService(), new FakeSeedRecoveryService(), new FakeEnableSeedRecoveryDialog(),
            new FakeChangeMasterPasswordDialog(), new FakeLocalizer(), new FakeLanguageStore(),
            new FakeThemeSwitcher(), NullLogger<SettingsViewModel>.Instance);

    private static SettingsViewModel CreateWithBackup(
        FakeBackupService backup,
        FakeArchiveExporter exporter,
        FakeFilePicker picker,
        FakeRestoreDialogService? restoreDialog = null,
        FakeSeedRecoveryService? seedRecovery = null,
        FakeEnableSeedRecoveryDialog? enableDialog = null,
        FakeChangeMasterPasswordDialog? changePasswordDialog = null) =>
        new(new FakeAiSettings(), new FakeOllamaCatalog(), new FakePlanningSettings(), new FakeAccountService(),
            new FakeSecretStore(), new FakeAiUsageLedger(), backup, exporter, picker,
            restoreDialog ?? new FakeRestoreDialogService(),
            seedRecovery ?? new FakeSeedRecoveryService(),
            enableDialog ?? new FakeEnableSeedRecoveryDialog(),
            changePasswordDialog ?? new FakeChangeMasterPasswordDialog(),
            new FakeLocalizer(), new FakeLanguageStore(), new FakeThemeSwitcher(),
            NullLogger<SettingsViewModel>.Instance);

    [Fact]
    public async Task Load_PopulatesBackupStatus()
    {
        var backup = new FakeBackupService { Status = new(new DateOnly(2026, 7, 3), 4, null) };
        var vm = CreateWithBackup(backup, new FakeArchiveExporter(), new FakeFilePicker());

        await vm.LoadCommand.ExecuteAsync(null);

        vm.LastDailySnapshotText.Should().Be("2026-07-03");
        vm.DailyBackupCountText.Should().Be("4");
        vm.HasBackupFailure.Should().BeFalse();
        vm.LastBackupFailureText.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_WithARecentSnapshot_ShowsItsAgeAndIsNotStale()
    {
        var newest = DateTime.UtcNow.AddHours(-3).AddMinutes(-5);
        var backup = new FakeBackupService { Status = new(new DateOnly(2026, 7, 3), 2, null, newest) };
        var vm = CreateWithBackup(backup, new FakeArchiveExporter(), new FakeFilePicker());

        await vm.LoadCommand.ExecuteAsync(null);

        vm.NewestSnapshotAgeText.Should().Be("Settings.Backup.Age.Hours");
        vm.IsBackupStale.Should().BeFalse();
        vm.LastDailySnapshotText.Should().Be(newest.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Load_WithAnOldSnapshot_FlagsItAsStale()
    {
        var backup = new FakeBackupService
        {
            Status = new(new DateOnly(2026, 7, 3), 2, null, DateTime.UtcNow.AddDays(-5)),
        };
        var vm = CreateWithBackup(backup, new FakeArchiveExporter(), new FakeFilePicker());

        await vm.LoadCommand.ExecuteAsync(null);

        vm.NewestSnapshotAgeText.Should().Be("Settings.Backup.Age.Days");
        vm.IsBackupStale.Should().BeTrue();
    }

    [Fact]
    public async Task Load_WithARecordedFailure_ShowsIt()
    {
        var backup = new FakeBackupService
        {
            Status = new(null, 0, null, null, new DateTime(2026, 7, 3, 8, 0, 0, DateTimeKind.Utc)),
        };
        var vm = CreateWithBackup(backup, new FakeArchiveExporter(), new FakeFilePicker());

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasBackupFailure.Should().BeTrue();
        vm.LastBackupFailureText.Should().Be("Settings.Backup.LastFailure");
        vm.LastDailySnapshotText.Should().Be("Settings.Backup.Never");
        vm.NewestSnapshotAgeText.Should().BeEmpty();
    }

    [Fact]
    public async Task BackupNow_WhenItFails_ShowsTheFailureInTheBackupPanel()
    {
        var backup = new FakeBackupService
        {
            NowThrow = new IOException("disk"),
            StatusAfterFailure = new(null, 0, null, null, DateTime.UtcNow),
        };
        var vm = CreateWithBackup(backup, new FakeArchiveExporter(), new FakeFilePicker());

        await vm.BackupNowCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Be("Settings.Backup.Status.Failed");
        vm.HasBackupFailure.Should().BeTrue();
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

    [Fact]
    public async Task RestoreFromSnapshot_OpensDialog_AndWhenStaged_ReportsRestart()
    {
        var dialog = new FakeRestoreDialogService { ResultStaged = true };
        var vm = CreateWithBackup(new FakeBackupService(), new FakeArchiveExporter(), new FakeFilePicker(), dialog);

        await vm.RestoreFromSnapshotCommand.ExecuteAsync(null);

        dialog.ShowCalls.Should().Be(1);
        vm.StatusMessage.Should().NotBeNullOrEmpty("a staged restore tells the owner to restart");
    }

    [Fact]
    public async Task RestoreFromSnapshot_WhenCancelled_ReportsNothing()
    {
        var dialog = new FakeRestoreDialogService { ResultStaged = false };
        var vm = CreateWithBackup(new FakeBackupService(), new FakeArchiveExporter(), new FakeFilePicker(), dialog);

        await vm.RestoreFromSnapshotCommand.ExecuteAsync(null);

        dialog.ShowCalls.Should().Be(1);
        vm.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_ReflectsSeedRecoveryEnabledState()
    {
        var seed = new FakeSeedRecoveryService { Enabled = true };
        var vm = CreateWithBackup(
            new FakeBackupService(), new FakeArchiveExporter(), new FakeFilePicker(), seedRecovery: seed);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.SeedRecoveryEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task EnableSeedRecovery_WhenDialogEnables_SetsStateAndStatus()
    {
        var dialog = new FakeEnableSeedRecoveryDialog { ResultEnabled = true };
        var vm = CreateWithBackup(
            new FakeBackupService(), new FakeArchiveExporter(), new FakeFilePicker(), enableDialog: dialog);

        await vm.EnableSeedRecoveryCommand.ExecuteAsync(null);

        dialog.ShowCalls.Should().Be(1);
        vm.SeedRecoveryEnabled.Should().BeTrue();
        vm.StatusMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EnableSeedRecovery_WhenCancelled_LeavesStateFalse()
    {
        var dialog = new FakeEnableSeedRecoveryDialog { ResultEnabled = false };
        var vm = CreateWithBackup(
            new FakeBackupService(), new FakeArchiveExporter(), new FakeFilePicker(), enableDialog: dialog);

        await vm.EnableSeedRecoveryCommand.ExecuteAsync(null);

        vm.SeedRecoveryEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ChangeMasterPassword_WhenChanged_ReportsSuccess()
    {
        var dialog = new FakeChangeMasterPasswordDialog { ResultChanged = true };
        var vm = CreateWithBackup(
            new FakeBackupService(), new FakeArchiveExporter(), new FakeFilePicker(), changePasswordDialog: dialog);

        await vm.ChangeMasterPasswordCommand.ExecuteAsync(null);

        dialog.ShowCalls.Should().Be(1);
        vm.StatusMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ChangeMasterPassword_WhenCancelled_ReportsNothing()
    {
        var dialog = new FakeChangeMasterPasswordDialog { ResultChanged = false };
        var vm = CreateWithBackup(
            new FakeBackupService(), new FakeArchiveExporter(), new FakeFilePicker(), changePasswordDialog: dialog);

        await vm.ChangeMasterPasswordCommand.ExecuteAsync(null);

        dialog.ShowCalls.Should().Be(1);
        vm.StatusMessage.Should().BeEmpty();
    }
}
