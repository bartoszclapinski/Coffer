using CommunityToolkit.Mvvm.ComponentModel;

namespace Coffer.Application.ViewModels.Setup;

public sealed partial class BipSeedVerificationStepViewModel : ObservableObject
{
    private readonly Func<string> _mnemonicAccessor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _word3 = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _word7 = "";

    public BipSeedVerificationStepViewModel(Func<string> mnemonicAccessor)
    {
        ArgumentNullException.ThrowIfNull(mnemonicAccessor);
        _mnemonicAccessor = mnemonicAccessor;
    }

    public bool IsValid
    {
        get
        {
            var mnemonic = _mnemonicAccessor();
            if (string.IsNullOrWhiteSpace(mnemonic))
            {
                return false;
            }

            var words = mnemonic.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 12)
            {
                return false;
            }

            return string.Equals(Word3.Trim(), words[2], StringComparison.OrdinalIgnoreCase)
                && string.Equals(Word7.Trim(), words[6], StringComparison.OrdinalIgnoreCase);
        }
    }

    public void ClearSensitive()
    {
        Word3 = "";
        Word7 = "";
    }
}
