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
/// View-model behind the Settings page (Phase 10-B): the provider + model of each AI role
/// (<see cref="Categorization"/>, <see cref="Analysis"/> — Sprint 31-D), the Ollama endpoint with its
/// connection test and model list, the masked API-key entry (stored in <see cref="ISecretStore"/>,
/// never read back — hard rule #6), the monthly PLN cap that feeds the budget gate, and the
/// month-to-date spend read from the cost ledger.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    // The daily snapshot runs on every launch, so a newest snapshot older than this means backups are not
    // happening (app not opened, or snapshots failing) and the age is highlighted.
    private static readonly TimeSpan _backupStaleAfter = TimeSpan.FromDays(2);

    private readonly IAiSettings _settings;
    private readonly IOllamaCatalog _ollamaCatalog;
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

    // The models the Ollama endpoint listed last; null until a listing succeeded.
    private IReadOnlyList<OllamaModelInfo>? _ollamaModels;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEndpointRemote))]
    [NotifyPropertyChangedFor(nameof(IsEndpointInvalid))]
    private string _ollamaEndpointText = AiDefaults.OllamaEndpoint;

    [ObservableProperty]
    private bool _isTestingConnection;

    [ObservableProperty]
    private string _connectionStatusText = "";

    [ObservableProperty]
    private bool _connectionSucceeded;

    [ObservableProperty]
    private bool _connectionFailed;

    [ObservableProperty]
    private bool _isRefreshingModels;

    [ObservableProperty]
    private string _ollamaModelsStatusText = "";

    [ObservableProperty]
    private bool _ollamaModelsFailed;

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
        IOllamaCatalog ollamaCatalog,
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
        ArgumentNullException.ThrowIfNull(ollamaCatalog);
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
        _ollamaCatalog = ollamaCatalog;
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

        Providers =
        [
            new AiProviderOption(AiDefaults.ClaudeProvider, "Settings.Ai.Provider.Claude", localizer["Settings.Ai.Provider.Claude"]),
            new AiProviderOption(AiDefaults.OllamaProvider, "Settings.Ai.Provider.Ollama", localizer["Settings.Ai.Provider.Ollama"]),
        ];
        Categorization = new AiRoleRouteViewModel(AiRole.Categorization, Providers, OnRoleProviderChanged);
        Analysis = new AiRoleRouteViewModel(AiRole.Analysis, Providers, OnRoleProviderChanged);

        // The provider labels are view-model data, so they follow a language switch here.
        _localizer.LanguageChanged += (_, _) =>
        {
            foreach (var provider in Providers)
            {
                provider.DisplayName = _localizer[provider.LabelKey];
            }
        };
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

    /// <summary>The providers a role can run on — only the wired ones (doc 04), shared by both role pickers.</summary>
    public IReadOnlyList<AiProviderOption> Providers { get; }

    /// <summary>Provider + model for batch categorisation.</summary>
    public AiRoleRouteViewModel Categorization { get; }

    /// <summary>Provider + model for chat, the advisor report, anomaly comments, cash-flow explanation and fallback parsing.</summary>
    public AiRoleRouteViewModel Analysis { get; }

    /// <summary>Whether any role runs on Claude — the API key is needed only then.</summary>
    public bool UsesClaude => Categorization.IsClaude || Analysis.IsClaude;

    /// <summary>The "no key saved" warning matters only while a role needs the key.</summary>
    public bool ShowMissingApiKey => !HasApiKey && UsesClaude;

    /// <summary>
    /// The endpoint text is a valid endpoint that is not this machine: prompts (redacted) leave the computer.
    /// </summary>
    public bool IsEndpointRemote =>
        OllamaEndpoint.TryParse(OllamaEndpointText, out var endpoint) && !OllamaEndpoint.IsLoopback(endpoint);

    /// <summary>The endpoint text is not a valid endpoint (Save, the connection test and the refresh refuse it).</summary>
    public bool IsEndpointInvalid => !OllamaEndpoint.TryParse(OllamaEndpointText, out _);

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(AppLanguage.Polish, "Polski"),
        new(AppLanguage.English, "English"),
    ];

    partial void OnHasApiKeyChanged(bool value) => OnPropertyChanged(nameof(ShowMissingApiKey));

    // The endpoint is being edited: an earlier test result no longer describes it.
    partial void OnOllamaEndpointTextChanged(string value)
    {
        ConnectionStatusText = "";
        ConnectionSucceeded = false;
        ConnectionFailed = false;
    }

    private void OnRoleProviderChanged(AiRoleRouteViewModel role)
    {
        OnPropertyChanged(nameof(UsesClaude));
        OnPropertyChanged(nameof(ShowMissingApiKey));

        // First switch to Ollama on this screen: fetch the model list so the picker has something to offer.
        // The refresh catches its own failures, so nothing escapes this fire-and-forget call.
        if (role.IsOllama && _ollamaModels is null && !IsRefreshingModels)
        {
            _ = RefreshOllamaModelsAsync();
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
        var loaded = false;
        try
        {
            var ct = CancellationToken.None;
            // A stored route the pickers cannot show (an unknown provider) shows the default, but is only
            // replaced if the owner then picks a provider or model (AiRoleRouteViewModel.IsEdited).
            Categorization.Load(await _settings.GetRouteAsync(AiRole.Categorization, ct).ConfigureAwait(true));
            Analysis.Load(await _settings.GetRouteAsync(AiRole.Analysis, ct).ConfigureAwait(true));
            OnPropertyChanged(nameof(UsesClaude));
            OnPropertyChanged(nameof(ShowMissingApiKey));
            OllamaEndpointText = OllamaEndpoint.Format(
                await _settings.GetOllamaEndpointAsync(ct).ConfigureAwait(true));

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
            loaded = true;
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

        // A role on Ollama needs the server's model list. It is fetched once the page is usable, so a slow or
        // absent server never holds up Save; a failure shows next to the endpoint, never as an exception.
        if (loaded && (Categorization.IsOllama || Analysis.IsOllama))
        {
            await RefreshOllamaModelsAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        // Validate before writing anything, so a refused save leaves storage untouched.
        if (!OllamaEndpoint.TryParse(OllamaEndpointText, out var endpoint))
        {
            StatusMessage = _localizer["Settings.Ai.Ollama.EndpointInvalid"];
            return;
        }

        if ((Categorization.IsEdited && Categorization.IsMissingModel)
            || (Analysis.IsEdited && Analysis.IsMissingModel))
        {
            StatusMessage = _localizer["Settings.Ai.Model.Required"];
            return;
        }

        IsBusy = true;
        StatusMessage = "";
        try
        {
            var ct = CancellationToken.None;
            foreach (var role in new[] { Categorization, Analysis })
            {
                if (role.IsEdited)
                {
                    await _settings.SetRouteAsync(role.Role, role.ToRoute(), ct).ConfigureAwait(true);
                    role.MarkSaved();
                }
            }

            await _settings.SetOllamaEndpointAsync(endpoint, ct).ConfigureAwait(true);
            OllamaEndpointText = OllamaEndpoint.Format(endpoint); // show the stored, canonical form
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

    [RelayCommand]
    private async Task TestOllamaConnectionAsync()
    {
        if (IsTestingConnection)
        {
            return;
        }

        ConnectionSucceeded = false;
        ConnectionFailed = false;
        // The typed address is tested, saved or not: the owner checks it before committing to it.
        if (!OllamaEndpoint.TryParse(OllamaEndpointText, out var endpoint))
        {
            ConnectionStatusText = _localizer["Settings.Ai.Ollama.EndpointInvalid"];
            ConnectionFailed = true;
            return;
        }

        IsTestingConnection = true;
        ConnectionStatusText = "";
        var connected = false;
        try
        {
            var result = await _ollamaCatalog.TestConnectionAsync(endpoint, CancellationToken.None).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                ConnectionStatusText = _localizer.Format("Settings.Ai.Ollama.Connected", result.Version ?? "");
                ConnectionSucceeded = true;
                connected = true;
            }
            else
            {
                ConnectionStatusText = UnavailableText(result.Failure);
                ConnectionFailed = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama connection test failed unexpectedly");
            ConnectionStatusText = UnavailableText(null);
            ConnectionFailed = true;
        }
        finally
        {
            IsTestingConnection = false;
        }

        // A server that answers is worth listing straight away.
        if (connected)
        {
            await RefreshOllamaModelsAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task RefreshOllamaModelsAsync()
    {
        if (IsRefreshingModels)
        {
            return;
        }

        OllamaModelsFailed = false;
        if (!OllamaEndpoint.TryParse(OllamaEndpointText, out var endpoint))
        {
            OllamaModelsStatusText = _localizer["Settings.Ai.Ollama.EndpointInvalid"];
            OllamaModelsFailed = true;
            return;
        }

        IsRefreshingModels = true;
        OllamaModelsStatusText = "";
        try
        {
            var models = await _ollamaCatalog.ListModelsAsync(endpoint, CancellationToken.None).ConfigureAwait(true);
            _ollamaModels = models;
            Categorization.SetOllamaModels(models);
            Analysis.SetOllamaModels(models);
            if (models.Count == 0)
            {
                OllamaModelsStatusText = _localizer["Settings.Ai.Ollama.NoModels"];
                OllamaModelsFailed = true;
            }
            else
            {
                OllamaModelsStatusText = _localizer.Format("Settings.Ai.Ollama.ModelsFound", models.Count);
            }
        }
        catch (AiProviderUnavailableException ex)
        {
            _logger.LogWarning("Listing Ollama models failed: {Reason}", ex.Reason);
            OllamaModelsStatusText = UnavailableText(ex.Reason);
            OllamaModelsFailed = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Listing Ollama models failed unexpectedly");
            OllamaModelsStatusText = UnavailableText(null);
            OllamaModelsFailed = true;
        }
        finally
        {
            IsRefreshingModels = false;
        }
    }

    private string UnavailableText(AiProviderUnavailableReason? reason) => reason switch
    {
        AiProviderUnavailableReason.Unreachable => _localizer["Settings.Ai.Ollama.Error.Unreachable"],
        AiProviderUnavailableReason.Timeout => _localizer["Settings.Ai.Ollama.Error.Timeout"],
        _ => _localizer["Settings.Ai.Ollama.Error.Rejected"],
    };

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
