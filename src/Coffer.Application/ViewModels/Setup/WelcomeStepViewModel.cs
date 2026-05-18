using CommunityToolkit.Mvvm.ComponentModel;

namespace Coffer.Application.ViewModels.Setup;

public sealed partial class WelcomeStepViewModel : ObservableObject
{
    public void ClearSensitive()
    {
        // No sensitive state on this step.
    }
}
