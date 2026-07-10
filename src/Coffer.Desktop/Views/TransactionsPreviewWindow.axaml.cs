using Avalonia.Controls;
using Avalonia.Interactivity;
using Coffer.Application.Localization;
using Coffer.Application.ViewModels.Transactions;
using Coffer.Core.Theming;
using Coffer.Desktop.Preview;
using Coffer.Desktop.Theme;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Desktop.Views;

/// <summary>Dev-only Transactions validation surface (Sprint 29-A). Env var <c>COFFER_TX_PREVIEW</c>.</summary>
public partial class TransactionsPreviewWindow : Window
{
    public TransactionsPreviewWindow()
    {
        InitializeComponent();
        DataContext = new PreviewShellState();

        var localizer = App.Services.GetRequiredService<ILocalizer>();
        var vm = new TransactionsViewModel(
            new PreviewTransactionsQuery(),
            new PreviewCategoryService(),
            localizer,
            NullLogger<TransactionsViewModel>.Instance);
        vm.LoadCommand.Execute(null);
        Section.DataContext = vm;
    }

    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        var next = ThemeManager.Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        ThemeManager.Apply(next);
    }

    private void OnToggleBlur(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PreviewShellState state)
        {
            state.HideBalances = !state.HideBalances;
        }
    }
}
