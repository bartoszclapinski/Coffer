using System.Collections.ObjectModel;
using Coffer.Application.Localization;
using Coffer.Core.Ai;
using Coffer.Core.Localization;
using Coffer.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Settings;

/// <summary>
/// View-model behind the Settings page (Phase 10-B): AI provider selection, the masked
/// API-key entry (stored in <see cref="ISecretStore"/>, never read back — hard rule #6),
/// the categorisation model, the monthly PLN cap that feeds the budget gate, and the
/// month-to-date spend read from the cost ledger.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAiSettings _settings;
    private readonly ISecretStore _secrets;
    private readonly IAiUsageLedger _ledger;
    private readonly ILocalizer _localizer;
    private readonly ILanguageStore _languageStore;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private string _selectedProvider = AiDefaults.ClaudeProvider;

    [ObservableProperty]
    private string _categorizationModel = AiDefaults.CategorizationModel;

    [ObservableProperty]
    private decimal _monthlyCapPln = AiDefaults.MonthlyCapPln;

    [ObservableProperty]
    private decimal _currentMonthSpendPln;

    [ObservableProperty]
    private bool _aiFallbackParsingEnabled = AiDefaults.AiFallbackParsingEnabled;

    [ObservableProperty]
    private string _ownerIdentityNames = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveApiKeyCommand))]
    private string _apiKeyInput = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearApiKeyCommand))]
    private bool _hasApiKey;

    public SettingsViewModel(
        IAiSettings settings,
        ISecretStore secrets,
        IAiUsageLedger ledger,
        ILocalizer localizer,
        ILanguageStore languageStore,
        ILogger<SettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(languageStore);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _secrets = secrets;
        _ledger = ledger;
        _localizer = localizer;
        _languageStore = languageStore;
        _logger = logger;

        _selectedLanguage = Languages.First(l => l.Language == localizer.Current);
    }

    public ObservableCollection<string> Providers { get; } =
        [AiDefaults.ClaudeProvider, AiDefaults.OpenAiProvider];

    public ObservableCollection<string> Models { get; } =
        ["claude-haiku-4-5", "claude-sonnet-4-6", "gpt-4o-mini", "gpt-4o"];

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(AppLanguage.Polish, "Polski"),
        new(AppLanguage.English, "English"),
    ];

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value is null || value.Language == _localizer.Current)
        {
            return;
        }

        _localizer.SetLanguage(value.Language);
        _languageStore.Save(value.Language);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var ct = CancellationToken.None;
            SelectedProvider = await _settings.GetActiveProviderAsync(ct).ConfigureAwait(true);
            CategorizationModel = await _settings.GetCategorizationModelAsync(ct).ConfigureAwait(true);
            MonthlyCapPln = await _settings.GetMonthlyCapPlnAsync(ct).ConfigureAwait(true);
            AiFallbackParsingEnabled = await _settings.GetAiFallbackParsingEnabledAsync(ct).ConfigureAwait(true);
            OwnerIdentityNames = await _settings.GetOwnerIdentityNamesAsync(ct).ConfigureAwait(true) ?? "";
            CurrentMonthSpendPln = await _ledger.GetCurrentMonthSpendPlnAsync(ct).ConfigureAwait(true);

            var key = await _secrets.GetSecretAsync(AiDefaults.ClaudeApiKeySecret, ct).ConfigureAwait(true);
            HasApiKey = !string.IsNullOrEmpty(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI settings");
            StatusMessage = _localizer["Settings.Status.LoadFailed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "";
        try
        {
            var ct = CancellationToken.None;
            await _settings.SetActiveProviderAsync(SelectedProvider, ct).ConfigureAwait(true);
            await _settings.SetCategorizationModelAsync(CategorizationModel, ct).ConfigureAwait(true);
            await _settings.SetMonthlyCapPlnAsync(MonthlyCapPln, ct).ConfigureAwait(true);
            await _settings.SetAiFallbackParsingEnabledAsync(AiFallbackParsingEnabled, ct).ConfigureAwait(true);
            await _settings.SetOwnerIdentityNamesAsync(OwnerIdentityNames, ct).ConfigureAwait(true);
            StatusMessage = _localizer["Settings.Status.Saved"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save AI settings");
            StatusMessage = _localizer["Settings.Status.SaveFailed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSaveApiKey() => !string.IsNullOrWhiteSpace(ApiKeyInput);

    [RelayCommand(CanExecute = nameof(CanSaveApiKey))]
    private async Task SaveApiKeyAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "";
        try
        {
            await _secrets
                .SetSecretAsync(AiDefaults.ClaudeApiKeySecret, ApiKeyInput.Trim(), CancellationToken.None)
                .ConfigureAwait(true);
            ApiKeyInput = "";
            HasApiKey = true;
            StatusMessage = _localizer["Settings.Status.KeySaved"];
        }
        catch (Exception ex)
        {
            // Never log the key itself (hard rule #6) — only that the write failed.
            _logger.LogError(ex, "Failed to store Claude API key");
            StatusMessage = _localizer["Settings.Status.KeySaveFailed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanClearApiKey() => HasApiKey;

    [RelayCommand(CanExecute = nameof(CanClearApiKey))]
    private async Task ClearApiKeyAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "";
        try
        {
            await _secrets
                .DeleteSecretAsync(AiDefaults.ClaudeApiKeySecret, CancellationToken.None)
                .ConfigureAwait(true);
            HasApiKey = false;
            StatusMessage = _localizer["Settings.Status.KeyCleared"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Claude API key");
            StatusMessage = _localizer["Settings.Status.KeyClearFailed"];
        }
        finally
        {
            IsBusy = false;
        }
    }
}
