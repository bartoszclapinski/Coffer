using Avalonia.Controls.ApplicationLifetimes;
using Coffer.Application.Dialogs;
using Coffer.Application.ViewModels.Restore;
using Coffer.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Coffer.Desktop.Platform;

/// <summary>
/// Avalonia implementation of <see cref="IRestoreDialogService"/>. Resolves the <see cref="RestoreWindow"/>
/// and its view-model from the container, loads the snapshot list, and shows the window modally over the
/// main window. Keeps all Avalonia window types inside Desktop so the Settings VM stays framework-free
/// (hard rule #4).
/// </summary>
public sealed class RestoreDialogService : IRestoreDialogService
{
    private readonly IServiceProvider _services;

    public RestoreDialogService(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public async Task<bool> ShowRestoreDialogAsync(CancellationToken ct)
    {
        var window = _services.GetRequiredService<RestoreWindow>();
        var vm = _services.GetRequiredService<RestoreViewModel>();
        window.DataContext = vm;

        await vm.LoadCommand.ExecuteAsync(null).ConfigureAwait(true);

        var owner = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is not null)
        {
            await window.ShowDialog(owner).ConfigureAwait(true);
        }
        else
        {
            window.Show();
        }

        return vm.Staged;
    }
}
