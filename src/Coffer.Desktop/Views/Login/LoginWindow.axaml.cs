using Avalonia.Controls;
using Avalonia.Input;
using Coffer.Application.ViewModels.Login;

namespace Coffer.Desktop.Views.Login;

public partial class LoginWindow : Window
{
    private LoginViewModel? _viewModel;

    public LoginWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        _viewModel = DataContext as LoginViewModel;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Block close while LoginWithPasswordAsync runs (~1-2s of Argon2).
        if (_viewModel?.IsBusy == true)
        {
            e.Cancel = true;
            return;
        }

        // Zero the typed password on close — covers both "logged in successfully,
        // window swap pending" and "user gave up and closed the window" paths.
        _viewModel?.ClearSensitive();
    }

    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter on the password field triggers the same default-button path as
        // clicking Zaloguj. IsDefault="True" on the button covers this for
        // Avalonia 11.* but the explicit handler is robust against XAML edits.
        if (e.Key == Key.Enter && _viewModel is { IsBusy: false } vm)
        {
            if (vm.LoginCommand.CanExecute(null))
            {
                vm.LoginCommand.Execute(null);
            }
            e.Handled = true;
        }
    }
}
