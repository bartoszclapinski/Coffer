using CommunityToolkit.Mvvm.ComponentModel;

namespace Coffer.Application.ViewModels.Setup;

public sealed partial class BipSeedDisplayStepViewModel : ObservableObject
{
    [ObservableProperty]
    private IReadOnlyList<string> _words = Array.Empty<string>();

    public void SetMnemonic(string mnemonic)
    {
        ArgumentNullException.ThrowIfNull(mnemonic);

        Words = string.IsNullOrWhiteSpace(mnemonic)
            ? Array.Empty<string>()
            : mnemonic.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    public void ClearSensitive()
    {
        Words = Array.Empty<string>();
    }
}
