using Coffer.Application.Localization;
using Coffer.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Recovery;

/// <summary>
/// View-model behind the restore-from-seed window (Sprint 25, doc 08 "Restore from seed"). Takes the 12-word
/// BIP39 seed and a new master password, calls <see cref="ISeedRecoveryService.RecoverWithSeedAsync"/> to
/// decrypt the DEK via the seed channel and reset the password, then raises <see cref="RecoveryCompleted"/>
/// so the shell can route into the app like a normal login. Never logs the seed or password (hard rule #6).
/// </summary>
public sealed partial class RestoreFromSeedViewModel : ObservableObject
{
    private readonly ISeedRecoveryService _seedRecovery;
    private readonly ISeedManager _seedManager;
    private readonly IPasswordStrengthChecker _strengthChecker;
    private readonly ILocalizer _localizer;
    private readonly ILogger<RestoreFromSeedViewModel> _logger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecoverCommand))]
    private string _seed = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordStrengthScore))]
    [NotifyCanExecuteChangedFor(nameof(RecoverCommand))]
    private string _newPassword = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecoverCommand))]
    private string _confirmPassword = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    public RestoreFromSeedViewModel(
        ISeedRecoveryService seedRecovery,
        ISeedManager seedManager,
        IPasswordStrengthChecker strengthChecker,
        ILocalizer localizer,
        ILogger<RestoreFromSeedViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(seedRecovery);
        ArgumentNullException.ThrowIfNull(seedManager);
        ArgumentNullException.ThrowIfNull(strengthChecker);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _seedRecovery = seedRecovery;
        _seedManager = seedManager;
        _strengthChecker = strengthChecker;
        _localizer = localizer;
        _logger = logger;
    }

    /// <summary>Raised after a successful recovery — the shell navigates into the app.</summary>
    public event EventHandler? RecoveryCompleted;

    public int PasswordStrengthScore => _strengthChecker.Evaluate(NewPassword).Score;

    private string NormalizedSeed =>
        string.Join(' ', Seed.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private bool CanRecover() =>
        !IsBusy && _seedManager.IsValid(NormalizedSeed) && IsPasswordAcceptable();

    [RelayCommand(CanExecute = nameof(CanRecover))]
    private async Task RecoverAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = "";
        try
        {
            // ConfigureAwait(true): RecoveryCompleted drives window construction in App on the UI thread.
            await _seedRecovery
                .RecoverWithSeedAsync(NormalizedSeed, NewPassword, CancellationToken.None)
                .ConfigureAwait(true);
            ClearSensitive();
            RecoveryCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidRecoverySeedException)
        {
            ErrorMessage = _localizer["Restore.Seed.Error.InvalidSeed"];
        }
        catch (SeedRecoveryUnavailableException)
        {
            ErrorMessage = _localizer["Restore.Seed.Error.Unavailable"];
        }
        catch (Exception ex) when (ex is VaultMissingException or VaultCorruptedException)
        {
            _logger.LogWarning(ex, "Seed recovery could not read the vault");
            ErrorMessage = _localizer["Restore.Seed.Error.Generic"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during seed recovery");
            ErrorMessage = _localizer["Restore.Seed.Error.Generic"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ClearSensitive()
    {
        Seed = "";
        NewPassword = "";
        ConfirmPassword = "";
    }

    private bool IsPasswordAcceptable()
    {
        if (NewPassword.Length < 12)
        {
            return false;
        }

        if (CountCharClasses(NewPassword) < 3)
        {
            return false;
        }

        if (PasswordStrengthScore < 3)
        {
            return false;
        }

        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            return false;
        }

        // The new master password must not be the recovery seed itself (doc 09).
        return !string.Equals(NewPassword.Trim(), NormalizedSeed, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountCharClasses(string password)
    {
        var hasLower = false;
        var hasUpper = false;
        var hasDigit = false;
        var hasSymbol = false;

        foreach (var ch in password)
        {
            if (char.IsLower(ch))
            {
                hasLower = true;
            }
            else if (char.IsUpper(ch))
            {
                hasUpper = true;
            }
            else if (char.IsDigit(ch))
            {
                hasDigit = true;
            }
            else
            {
                hasSymbol = true;
            }
        }

        var count = 0;
        if (hasLower)
        {
            count++;
        }

        if (hasUpper)
        {
            count++;
        }

        if (hasDigit)
        {
            count++;
        }

        if (hasSymbol)
        {
            count++;
        }

        return count;
    }
}
