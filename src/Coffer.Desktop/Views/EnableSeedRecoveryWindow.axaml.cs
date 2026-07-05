using Avalonia.Controls;
using Coffer.Application.ViewModels.Recovery;

namespace Coffer.Desktop.Views;

public partial class EnableSeedRecoveryWindow : Window
{
    private EnableSeedRecoveryViewModel? _viewModel;

    public EnableSeedRecoveryWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        _viewModel = DataContext as EnableSeedRecoveryViewModel;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Block close while EnableSeedRecoveryAsync runs.
        if (_viewModel?.IsBusy == true)
        {
            e.Cancel = true;
            return;
        }

        // Zero the displayed seed + verification input on close.
        _viewModel?.ClearSensitive();
    }
}
