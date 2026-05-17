using Avalonia.Controls;
using Coffer.Application.ViewModels.Setup;

namespace Coffer.Desktop.Views.Setup;

public partial class SetupWizardWindow : Window
{
    private SetupWizardViewModel? _viewModel;

    public SetupWizardWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        _viewModel = DataContext as SetupWizardViewModel;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Block close while the setup is committing — prevents a bricked state from
        // a window kill during CompleteSetupAsync (1-2 s of Argon2 + migration).
        if (_viewModel?.IsBusy == true)
        {
            e.Cancel = true;
        }
    }
}
