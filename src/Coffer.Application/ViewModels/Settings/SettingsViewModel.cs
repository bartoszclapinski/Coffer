using System.Collections.ObjectModel;
using System.Globalization;
using Coffer.Application.Dialogs;
using Coffer.Application.Localization;
using Coffer.Core.Accounts;
using Coffer.Core.Ai;
using Coffer.Core.Backup;
using Coffer.Core.Import;
using Coffer.Core.Localization;
using Coffer.Core.Planning;
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
    private readonly IPlanningSettings _planningSettings;
    private readonly IAccountService _accountService;
    private readonly ISecretStore _secrets;
    private readonly IAiUsageLedger _ledger;
    private readonly IBackupService _backupService;
    private readonly IArchiveExporter _archiveExporter;
    private readonly IFilePicker _filePicker;
    private readonly IRestoreDialogService _restoreDialog;
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
    private decimal _safetyFloorPln = PlanningDefaults.SafetyFloorPln;

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

    [ObservableProperty]
    private string _lastDailySnapshotText = "";

    [ObservableProperty]
    private string _dailyBackupCountText = "";

    public SettingsViewModel(
        IAiSettings settings,
        IPlanningSettings planningSettings,
        IAccountService accountService,
        ISecretStore secrets,
        IAiUsageLedger ledger,
        IBackupService backupService,
        IArchiveExporter archiveExporter,
        IFilePicker filePicker,
        IRestoreDialogService restoreDialog,
        ILocalizer localizer,
        ILanguageStore languageStore,
        ILogger<SettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(planningSettings);
        ArgumentNullException.ThrowIfNull(accountService);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(backupService);
        ArgumentNullException.ThrowIfNull(archiveExporter);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(restoreDialog);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(languageStore);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _planningSettings = planningSettings;
        _accountService = accountService;
        _secrets = secrets;
        _ledger = ledger;
        _backupService = backupService;
        _archiveExporter = archiveExporter;
        _filePicker = filePicker;
        _restoreDialog = restoreDialog;
        _localizer = localizer;
        _languageStore = languageStore;
        _logger = logger;

        _selectedLanguage = Languages.First(l => l.Language == localizer.Current);
    }

    public ObservableCollection<AccountAnchorRowViewModel> Accounts { get; } = [];

    public bool HasAccounts => Accounts.Count > 0;

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
            SafetyFloorPln = await _planningSettings.GetSafetyFloorPlnAsync(ct).ConfigureAwait(true);
            AiFallbackParsingEnabled = await _settings.GetAiFallbackParsingEnabledAsync(ct).ConfigureAwait(true);
            OwnerIdentityNames = await _settings.GetOwnerIdentityNamesAsync(ct).ConfigureAwait(true) ?? "";
            CurrentMonthSpendPln = await _ledger.GetCurrentMonthSpendPlnAsync(ct).ConfigureAwait(true);

            await LoadAccountsAsync(ct).ConfigureAwait(true);

            var key = await _secrets.GetSecretAsync(AiDefaults.ClaudeApiKeySecret, ct).ConfigureAwait(true);
            HasApiKey = !string.IsNullOrEmpty(key);

            await LoadBackupStatusAsync(ct).ConfigureAwait(true);
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
            await _planningSettings.SetSafetyFloorPlnAsync(SafetyFloorPln, ct).ConfigureAwait(true);
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

    private async Task LoadAccountsAsync(CancellationToken ct)
    {
        var accounts = await _accountService.GetAllWithAnchorsAsync(ct).ConfigureAwait(true);

        Accounts.Clear();
        foreach (var a in accounts)
        {
            Accounts.Add(new AccountAnchorRowViewModel(
                a.Id, a.Name, a.BankCode, a.AnchorDate, a.AnchorBalance, SaveAnchorAsync, ClearAnchorAsync));
        }

        OnPropertyChanged(nameof(HasAccounts));
    }

    private async Task SaveAnchorAsync(AccountAnchorRowViewModel row)
    {
        if (row.IsBusy)
        {
            return;
        }

        // Anchoring needs both the real balance and the date it was true.
        if (row.AnchorDate is not { } offset)
        {
            StatusMessage = _localizer["Settings.Anchor.DateRequired"];
            return;
        }

        var date = DateOnly.FromDateTime(offset.Date);
        if (date > DateOnly.FromDateTime(DateTime.Today))
        {
            StatusMessage = _localizer["Settings.Anchor.FutureDate"];
            return;
        }

        row.IsBusy = true;
        StatusMessage = "";
        try
        {
            await _accountService
                .SetBalanceAnchorAsync(row.Id, row.AnchorBalance, date, CancellationToken.None)
                .ConfigureAwait(true);
            row.HasAnchor = true;
            StatusMessage = _localizer["Settings.Anchor.Saved"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save balance anchor for account {AccountId}", row.Id);
            StatusMessage = _localizer["Settings.Anchor.SaveFailed"];
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    private async Task ClearAnchorAsync(AccountAnchorRowViewModel row)
    {
        if (row.IsBusy)
        {
            return;
        }

        row.IsBusy = true;
        StatusMessage = "";
        try
        {
            await _accountService
                .SetBalanceAnchorAsync(row.Id, balance: null, date: null, CancellationToken.None)
                .ConfigureAwait(true);
            row.HasAnchor = false;
            row.AnchorBalance = 0m;
            row.AnchorDate = null;
            StatusMessage = _localizer["Settings.Anchor.Cleared"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear balance anchor for account {AccountId}", row.Id);
            StatusMessage = _localizer["Settings.Anchor.SaveFailed"];
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "";
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            await _backupService.CreateSnapshotNowAsync(today, CancellationToken.None).ConfigureAwait(true);
            await LoadBackupStatusAsync(CancellationToken.None).ConfigureAwait(true);
            StatusMessage = _localizer["Settings.Backup.Status.Done"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create a backup snapshot");
            StatusMessage = _localizer["Settings.Backup.Status.Failed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportArchiveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var suggested = $"Coffer-archive-{DateOnly.FromDateTime(DateTime.Now):yyyy-MM-dd}.zip";
        var target = await _filePicker.PickSaveArchiveFileAsync(suggested, CancellationToken.None).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(target))
        {
            return; // user cancelled
        }

        IsBusy = true;
        StatusMessage = "";
        try
        {
            await _archiveExporter.ExportAsync(target, CancellationToken.None).ConfigureAwait(true);
            StatusMessage = _localizer["Settings.Backup.Export.Done"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export the archive");
            StatusMessage = _localizer["Settings.Backup.Export.Failed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreFromSnapshotAsync()
    {
        if (IsBusy)
        {
            return;
        }

        StatusMessage = "";
        var staged = await _restoreDialog.ShowRestoreDialogAsync(CancellationToken.None).ConfigureAwait(true);
        if (staged)
        {
            // The swap runs at the next startup, before the database opens — tell the owner to restart.
            StatusMessage = _localizer["Settings.Restore.Staged"];
        }
    }

    private async Task LoadBackupStatusAsync(CancellationToken ct)
    {
        var status = await _backupService.GetStatusAsync(ct).ConfigureAwait(true);
        LastDailySnapshotText = status.LastDailySnapshot is { } date
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : _localizer["Settings.Backup.Never"];
        DailyBackupCountText = status.DailyCount.ToString(CultureInfo.InvariantCulture);
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
