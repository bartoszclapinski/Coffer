using System.Collections.ObjectModel;
using System.Globalization;
using Coffer.Application.Dialogs;
using Coffer.Application.Localization;
using Coffer.Application.Theming;
using Coffer.Core.Accounts;
using Coffer.Core.Ai;
using Coffer.Core.Backup;
using Coffer.Core.Import;
using Coffer.Core.Localization;
using Coffer.Core.Planning;
using Coffer.Core.Security;
using Coffer.Core.Theming;
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
    // The daily snapshot runs on every launch, so a newest snapshot older than this means backups are not
    // happening (app not opened, or snapshots failing) and the age is highlighted.
    private static readonly TimeSpan _backupStaleAfter = TimeSpan.FromDays(2);

    private readonly IAiSettings _settings;
    private readonly IPlanningSettings _planningSettings;
    private readonly IAccountService _accountService;
    private readonly ISecretStore _secrets;
    private readonly IAiUsageLedger _ledger;
    private readonly IBackupService _backupService;
    private readonly IArchiveExporter _archiveExporter;
    private readonly IFilePicker _filePicker;
    private readonly IRestoreDialogService _restoreDialog;
    private readonly ISeedRecoveryService _seedRecovery;
    private readonly IEnableSeedRecoveryDialog _enableSeedRecoveryDialog;
    private readonly IChangeMasterPasswordDialog _changeMasterPasswordDialog;
    private readonly ILocalizer _localizer;
    private readonly ILanguageStore _languageStore;
    private readonly IThemeSwitcher _themeSwitcher;
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

    // True once the owner picks a provider or model on this screen. Save writes the route only then:
    // the pickers offer Claude only (until 31-D), so a stored route they cannot show (e.g. a local
    // Ollama route) is displayed as Claude and must not be overwritten when another setting is saved.
    private bool _routeEdited;

    // Set while Load fills the pickers, so loading does not count as an edit.
    private bool _loadingRoute;

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

    [ObservableProperty]
    private string _newestSnapshotAgeText = "";

    [ObservableProperty]
    private bool _isBackupStale;

    [ObservableProperty]
    private string _lastBackupFailureText = "";

    [ObservableProperty]
    private bool _hasBackupFailure;

    [ObservableProperty]
    private bool _seedRecoveryEnabled;

    [ObservableProperty]
    private bool _isDarkTheme;

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
        ISeedRecoveryService seedRecovery,
        IEnableSeedRecoveryDialog enableSeedRecoveryDialog,
        IChangeMasterPasswordDialog changeMasterPasswordDialog,
        ILocalizer localizer,
        ILanguageStore languageStore,
        IThemeSwitcher themeSwitcher,
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
        ArgumentNullException.ThrowIfNull(seedRecovery);
        ArgumentNullException.ThrowIfNull(enableSeedRecoveryDialog);
        ArgumentNullException.ThrowIfNull(changeMasterPasswordDialog);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(languageStore);
        ArgumentNullException.ThrowIfNull(themeSwitcher);
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
        _seedRecovery = seedRecovery;
        _enableSeedRecoveryDialog = enableSeedRecoveryDialog;
        _changeMasterPasswordDialog = changeMasterPasswordDialog;
        _localizer = localizer;
        _languageStore = languageStore;
        _themeSwitcher = themeSwitcher;
        _logger = logger;

        _selectedLanguage = Languages.First(l => l.Language == localizer.Current);
        _isDarkTheme = themeSwitcher.Current == AppTheme.Dark;

        // Keep the toggle in sync if the theme is flipped elsewhere (top-bar button, command palette)
        // while the Settings page is open.
        _themeSwitcher.Changed += (_, _) => IsDarkTheme = _themeSwitcher.Current == AppTheme.Dark;
    }

    // Applying the switcher persists the choice. When the change came from the switcher itself
    // (the Changed handler above) the theme already matches, so this does nothing.
    partial void OnIsDarkThemeChanged(bool value)
    {
        var target = value ? AppTheme.Dark : AppTheme.Light;
        if (_themeSwitcher.Current != target)
        {
            _themeSwitcher.Set(target);
        }
    }

    public ObservableCollection<AccountAnchorRowViewModel> Accounts { get; } = [];

    public bool HasAccounts => Accounts.Count > 0;

    // Only providers that are actually wired (doc 04).
    public ObservableCollection<string> Providers { get; } = [AiDefaults.ClaudeProvider];

    // Current generation first; the 4.x models stay selectable as a fallback.
    public ObservableCollection<string> Models { get; } = new(AiDefaults.ClaudeModels);

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(AppLanguage.Polish, "Polski"),
        new(AppLanguage.English, "English"),
    ];

    partial void OnSelectedProviderChanged(string value) => MarkRouteEdited();

    partial void OnCategorizationModelChanged(string value) => MarkRouteEdited();

    private void MarkRouteEdited()
    {
        if (!_loadingRoute)
        {
            _routeEdited = true;
        }
    }

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
            // A stored route the pickers do not offer shows the default, but is only replaced if the
            // owner then picks a provider or model (see _routeEdited). Only the categorisation route
            // is editable here; the per-role pickers arrive in Sprint 31-D.
            var route = await _settings.GetRouteAsync(AiRole.Categorization, ct).ConfigureAwait(true);
            _loadingRoute = true;
            try
            {
                SelectedProvider = OfferedOrDefault(Providers, route.Provider, AiDefaults.ClaudeProvider);
                CategorizationModel = OfferedOrDefault(Models, route.Model, AiDefaults.CategorizationModel);
                _routeEdited = false;
            }
            finally
            {
                _loadingRoute = false;
            }

            MonthlyCapPln = await _settings.GetMonthlyCapPlnAsync(ct).ConfigureAwait(true);
            SafetyFloorPln = await _planningSettings.GetSafetyFloorPlnAsync(ct).ConfigureAwait(true);
            AiFallbackParsingEnabled = await _settings.GetAiFallbackParsingEnabledAsync(ct).ConfigureAwait(true);
            OwnerIdentityNames = await _settings.GetOwnerIdentityNamesAsync(ct).ConfigureAwait(true) ?? "";
            CurrentMonthSpendPln = await _ledger.GetCurrentMonthSpendPlnAsync(ct).ConfigureAwait(true);

            await LoadAccountsAsync(ct).ConfigureAwait(true);

            var key = await _secrets.GetSecretAsync(AiDefaults.ClaudeApiKeySecret, ct).ConfigureAwait(true);
            HasApiKey = !string.IsNullOrEmpty(key);

            await LoadBackupStatusAsync(ct).ConfigureAwait(true);
            SeedRecoveryEnabled = await _seedRecovery.IsSeedRecoveryEnabledAsync(ct).ConfigureAwait(true);
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
            if (_routeEdited)
            {
                await _settings.SetRouteAsync(
                    AiRole.Categorization, new AiRoute(SelectedProvider, CategorizationModel), ct).ConfigureAwait(true);
                _routeEdited = false;
            }

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
            await TryRefreshBackupStatusAsync().ConfigureAwait(true);
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

    [RelayCommand]
    private async Task ChangeMasterPasswordAsync()
    {
        if (IsBusy)
        {
            return;
        }

        StatusMessage = "";
        var changed = await _changeMasterPasswordDialog.ShowAsync(CancellationToken.None).ConfigureAwait(true);
        if (changed)
        {
            StatusMessage = _localizer["Settings.Password.Changed"];
        }
    }

    [RelayCommand]
    private async Task EnableSeedRecoveryAsync()
    {
        if (IsBusy || SeedRecoveryEnabled)
        {
            return;
        }

        StatusMessage = "";
        var enabled = await _enableSeedRecoveryDialog.ShowAsync(CancellationToken.None).ConfigureAwait(true);
        if (enabled)
        {
            SeedRecoveryEnabled = true;
            StatusMessage = _localizer["Settings.SeedRecovery.Enabled"];
        }
    }

    private async Task LoadBackupStatusAsync(CancellationToken ct)
    {
        var status = await _backupService.GetStatusAsync(ct).ConfigureAwait(true);
        var nowUtc = DateTime.UtcNow;

        if (status.NewestSnapshotAtUtc is { } newestUtc)
        {
            var age = nowUtc - newestUtc;
            LastDailySnapshotText = FormatLocalTimestamp(newestUtc);
            NewestSnapshotAgeText = FormatAge(age);
            IsBackupStale = age > _backupStaleAfter;
        }
        else
        {
            LastDailySnapshotText = status.LastDailySnapshot is { } date
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : _localizer["Settings.Backup.Never"];
            NewestSnapshotAgeText = "";
            IsBackupStale = false;
        }

        DailyBackupCountText = status.DailyCount.ToString(CultureInfo.InvariantCulture);

        HasBackupFailure = status.LastFailureAtUtc is not null;
        LastBackupFailureText = status.LastFailureAtUtc is { } failedUtc
            ? _localizer.Format("Settings.Backup.LastFailure", FormatLocalTimestamp(failedUtc))
            : "";
    }

    // After a failed "Back up now" the service has recorded the failure; show it, but never let a second
    // failure (reading the status) mask the first one's message.
    private async Task TryRefreshBackupStatusAsync()
    {
        try
        {
            await LoadBackupStatusAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh the backup status after a failed snapshot");
        }
    }

    private static string OfferedOrDefault(IEnumerable<string> offered, string stored, string fallback) =>
        offered.Contains(stored, StringComparer.OrdinalIgnoreCase) ? stored : fallback;

    private static string FormatLocalTimestamp(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1))
        {
            // Includes a small negative age from clock skew between the file stamp and now.
            return _localizer["Settings.Backup.Age.JustNow"];
        }

        if (age < TimeSpan.FromHours(1))
        {
            return _localizer.Format("Settings.Backup.Age.Minutes", (int)age.TotalMinutes);
        }

        if (age < TimeSpan.FromDays(1))
        {
            return _localizer.Format("Settings.Backup.Age.Hours", (int)age.TotalHours);
        }

        return age < TimeSpan.FromDays(2)
            ? _localizer["Settings.Backup.Age.OneDay"]
            : _localizer.Format("Settings.Backup.Age.Days", (int)age.TotalDays);
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
