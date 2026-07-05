using Avalonia.Controls;
using Coffer.Application.ViewModels.Restore;

namespace Coffer.Desktop.Views;

public partial class RestoreWindow : Window
{
    private RestoreViewModel? _viewModel;

    public RestoreWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        _viewModel = DataContext as RestoreViewModel;

        if (_viewModel is not null)
        {
            _viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, System.EventArgs e) => Close();
}
