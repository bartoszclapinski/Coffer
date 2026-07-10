using Avalonia.Controls;
using Avalonia.Interactivity;
using Coffer.Application.Localization;
using Coffer.Application.ViewModels.Dashboard;
using Coffer.Core.Theming;
using Coffer.Desktop.Preview;
using Coffer.Desktop.Theme;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Desktop.Views;

/// <summary>
/// Dev-only Overview validation surface (Sprint 28-C). Renders <see cref="DashboardView"/> with
/// canned data (no DB, no login) so the screen can be reviewed in both themes. Launched only via
/// the <c>COFFER_OVERVIEW_PREVIEW</c> environment variable.
/// </summary>
public partial class OverviewPreviewWindow : Window
{
    public OverviewPreviewWindow()
    {
        InitializeComponent();

        // The window DataContext carries HideBalances so DashboardView's
        // "$parent[Window].DataContext.HideBalances" privacy-blur binding resolves here.
        DataContext = new PreviewShellState();

        var localizer = App.Services.GetRequiredService<ILocalizer>();
        var vm = new DashboardViewModel(new PreviewDashboardQuery(), localizer, NullLogger<DashboardViewModel>.Instance);
        vm.LoadCommand.Execute(null);
        Dash.DataContext = vm;
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

    private sealed partial class PreviewShellState : ObservableObject
    {
        [ObservableProperty]
        private bool _hideBalances;
    }
}
