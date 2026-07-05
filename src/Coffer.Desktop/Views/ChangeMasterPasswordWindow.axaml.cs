using Avalonia.Controls;
using Coffer.Application.ViewModels.Security;

namespace Coffer.Desktop.Views;

public partial class ChangeMasterPasswordWindow : Window
{
    private ChangeMasterPasswordViewModel? _viewModel;

    public ChangeMasterPasswordWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        _viewModel = DataContext as ChangeMasterPasswordViewModel;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Block close while ChangeMasterPasswordAsync runs (~1-2s of Argon2, twice).
        if (_viewModel?.IsBusy == true)
        {
            e.Cancel = true;
            return;
        }

        _viewModel?.ClearSensitive();
    }
}
