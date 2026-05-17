using Coffer.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Coffer.Application.ViewModels.Setup;

public sealed partial class MasterPasswordStepViewModel : ObservableObject
{
    private readonly IPasswordStrengthChecker _strengthChecker;
    private readonly Func<string> _mnemonicAccessor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(Strength))]
    private string _password = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _confirmation = "";

    public MasterPasswordStepViewModel(
        IPasswordStrengthChecker strengthChecker,
        Func<string> mnemonicAccessor)
    {
        ArgumentNullException.ThrowIfNull(strengthChecker);
        ArgumentNullException.ThrowIfNull(mnemonicAccessor);

        _strengthChecker = strengthChecker;
        _mnemonicAccessor = mnemonicAccessor;
    }

    public PasswordStrength Strength => _strengthChecker.Evaluate(Password);

    public bool IsValid
    {
        get
        {
            if (Password.Length < 12)
            {
                return false;
            }

            if (CountCharClasses(Password) < 3)
            {
                return false;
            }

            if (Strength.Score < 3)
            {
                return false;
            }

            if (!string.Equals(Password, Confirmation, StringComparison.Ordinal))
            {
                return false;
            }

            var mnemonic = _mnemonicAccessor();
            if (!string.IsNullOrEmpty(mnemonic) &&
                string.Equals(Password.Trim(), mnemonic, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }
    }

    public void ClearSensitive()
    {
        Password = "";
        Confirmation = "";
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
