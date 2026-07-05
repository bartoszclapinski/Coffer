using Coffer.Application.Localization;
using Coffer.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Security;

/// <summary>
/// View-model behind the "change master password" dialog (Sprint 26). Takes the current password (to prove
/// it's the owner) and a new password (validated with the setup strength rules), calls
/// <see cref="IMasterPasswordService.ChangeMasterPasswordAsync"/>, then raises <see cref="Completed"/>.
/// Never logs any password (hard rule #6).
/// </summary>
public sealed partial class ChangeMasterPasswordViewModel : ObservableObject
{
    private readonly IMasterPasswordService _masterPassword;
    private readonly IPasswordStrengthChecker _strengthChecker;
    private readonly ILocalizer _localizer;
    private readonly ILogger<ChangeMasterPasswordViewModel> _logger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeCommand))]
    private string _currentPassword = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordStrengthScore))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCommand))]
    private string _newPassword = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeCommand))]
    private string _confirmPassword = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    public ChangeMasterPasswordViewModel(
        IMasterPasswordService masterPassword,
        IPasswordStrengthChecker strengthChecker,
        ILocalizer localizer,
        ILogger<ChangeMasterPasswordViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);
        ArgumentNullException.ThrowIfNull(strengthChecker);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _masterPassword = masterPassword;
        _strengthChecker = strengthChecker;
        _localizer = localizer;
        _logger = logger;
    }

    public event EventHandler? Completed;

    public event EventHandler? CancelRequested;

    public int PasswordStrengthScore => _strengthChecker.Evaluate(NewPassword).Score;

    /// <summary>Whether the password was successfully changed before the dialog closed.</summary>
    public bool Changed { get; private set; }

    private bool CanChange() =>
        !IsBusy && CurrentPassword.Length > 0 && IsNewPasswordAcceptable();

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task ChangeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = "";
        try
        {
            await _masterPassword
                .ChangeMasterPasswordAsync(CurrentPassword, NewPassword, CancellationToken.None)
                .ConfigureAwait(true);
            Changed = true;
            ClearSensitive();
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidMasterPasswordException)
        {
            ErrorMessage = _localizer["ChangePassword.Error.WrongCurrent"];
            CurrentPassword = "";
        }
        catch (Exception ex) when (ex is VaultMissingException or VaultCorruptedException)
        {
            _logger.LogWarning(ex, "Password change could not read the vault");
            ErrorMessage = _localizer["ChangePassword.Error.Generic"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error changing the master password");
            ErrorMessage = _localizer["ChangePassword.Error.Generic"];
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
        CurrentPassword = "";
        NewPassword = "";
        ConfirmPassword = "";
    }

    private bool IsNewPasswordAcceptable()
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

        // A "change" that keeps the same password is pointless — require a different one.
        return !string.Equals(NewPassword, CurrentPassword, StringComparison.Ordinal);
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
