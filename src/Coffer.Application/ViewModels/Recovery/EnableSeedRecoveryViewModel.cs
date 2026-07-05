using Coffer.Application.Localization;
using Coffer.Application.ViewModels.Setup;
using Coffer.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Recovery;

/// <summary>
/// View-model behind the "enable seed recovery" dialog (Sprint 25). The v1 setup seed was shown but never
/// wrapped anything, so it is cryptographically inert — enabling therefore <em>mints a fresh functional
/// seed</em>: it is generated, displayed (screen-capture-blocked), and verified (like setup), then used to
/// wrap the in-memory DEK via <see cref="ISeedRecoveryService.EnableSeedRecoveryAsync"/>. Reuses the setup
/// seed-display and verification step VMs. Never logs the seed (hard rule #6).
/// </summary>
public sealed partial class EnableSeedRecoveryViewModel : ObservableObject
{
    private readonly ISeedRecoveryService _seedRecovery;
    private readonly ILocalizer _localizer;
    private readonly ILogger<EnableSeedRecoveryViewModel> _logger;
    private readonly string _mnemonic;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = "";

    public EnableSeedRecoveryViewModel(
        ISeedRecoveryService seedRecovery,
        ISeedManager seedManager,
        ILocalizer localizer,
        ILogger<EnableSeedRecoveryViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(seedRecovery);
        ArgumentNullException.ThrowIfNull(seedManager);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _seedRecovery = seedRecovery;
        _localizer = localizer;
        _logger = logger;

        _mnemonic = seedManager.GenerateMnemonic();
        Display = new BipSeedDisplayStepViewModel();
        Display.SetMnemonic(_mnemonic);
        Verification = new BipSeedVerificationStepViewModel(() => _mnemonic);
        Verification.PropertyChanged += (_, _) => EnableCommand.NotifyCanExecuteChanged();
    }

    public BipSeedDisplayStepViewModel Display { get; }

    public BipSeedVerificationStepViewModel Verification { get; }

    /// <summary>Whether seed recovery was successfully enabled before the dialog closed.</summary>
    public bool Enabled { get; private set; }

    public event EventHandler? Completed;

    public event EventHandler? CancelRequested;

    private bool CanEnable() => !IsBusy && Verification.IsValid;

    [RelayCommand(CanExecute = nameof(CanEnable))]
    private async Task EnableAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = "";
        try
        {
            await _seedRecovery.EnableSeedRecoveryAsync(_mnemonic, CancellationToken.None).ConfigureAwait(true);
            Enabled = true;
            ClearSensitive();
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable seed recovery");
            ErrorMessage = _localizer["Settings.SeedRecovery.EnableFailed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke(this, EventArgs.Empty);

    public void ClearSensitive()
    {
        Display.ClearSensitive();
        Verification.ClearSensitive();
    }
}
