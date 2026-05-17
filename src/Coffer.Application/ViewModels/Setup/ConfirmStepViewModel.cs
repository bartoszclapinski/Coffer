using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coffer.Application.ViewModels.Setup;

public sealed partial class ConfirmStepViewModel : ObservableObject
{
    private const string _genericFailureMessage = "Nie udało się utworzyć sejfu. Spróbuj ponownie.";

    private readonly Func<Task> _completeAction;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = "";

    public ConfirmStepViewModel(Func<Task> completeAction)
    {
        ArgumentNullException.ThrowIfNull(completeAction);
        _completeAction = completeAction;
    }

    [RelayCommand]
    private async Task CreateVaultAsync()
    {
        IsBusy = true;
        ErrorMessage = "";
        try
        {
            await _completeAction().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The full exception is logged by SetupService; the UI shows a generic message
            // to avoid leaking internal failure details to the user.
            ErrorMessage = _genericFailureMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ClearSensitive()
    {
        // No sensitive state on this step.
    }
}
