using Avalonia.Controls;
using Coffer.Application.ViewModels.Recovery;

namespace Coffer.Desktop.Views;

public partial class RestoreFromSeedWindow : Window
{
    private RestoreFromSeedViewModel? _viewModel;

    public RestoreFromSeedWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        _viewModel = DataContext as RestoreFromSeedViewModel;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Block close while RecoverWithSeedAsync runs (~1-2s of Argon2).
        if (_viewModel?.IsBusy == true)
        {
            e.Cancel = true;
            return;
        }

        // Zero the typed seed + passwords on close, whichever path got here.
        _viewModel?.ClearSensitive();
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
