using Coffer.Application.Localization;
using Coffer.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coffer.Application.ViewModels.Setup;

public sealed partial class ConfirmStepViewModel : ObservableObject
{
    private readonly Func<Task> _completeAction;
    private readonly ILocalizer _localizer;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = "";

    public ConfirmStepViewModel(Func<Task> completeAction, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(completeAction);
        ArgumentNullException.ThrowIfNull(localizer);

        _completeAction = completeAction;
        _localizer = localizer;
    }

    [RelayCommand]
    private async Task CreateVaultAsync()
    {
        IsBusy = true;
        ErrorMessage = "";
        try
        {
            await _completeAction().ConfigureAwait(true);
        }
        catch (VaultAlreadyExistsException ex)
        {
            // Specific failure surfaces the offending path so the user can investigate
            // the existing vault rather than blindly retrying.
            ErrorMessage = _localizer.Format("Setup.Confirm.Error.VaultExists", ex.FilePath);
        }
        catch (Exception)
        {
            // The full exception is logged by SetupService; the UI shows a generic message
            // to avoid leaking internal failure details to the user.
            ErrorMessage = _localizer["Setup.Confirm.Error.Generic"];
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
