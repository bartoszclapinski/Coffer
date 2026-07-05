using Avalonia.Controls.ApplicationLifetimes;
using Coffer.Application.Dialogs;
using Coffer.Application.ViewModels.Security;
using Coffer.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Coffer.Desktop.Platform;

/// <summary>
/// Avalonia implementation of <see cref="IChangeMasterPasswordDialog"/>. Resolves the
/// <see cref="ChangeMasterPasswordWindow"/> + view-model and shows it modally over the main window. Keeps
/// Avalonia window types inside Desktop so the Settings VM stays framework-free (hard rule #4).
/// </summary>
public sealed class ChangeMasterPasswordDialogService : IChangeMasterPasswordDialog
{
    private readonly IServiceProvider _services;

    public ChangeMasterPasswordDialogService(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public async Task<bool> ShowAsync(CancellationToken ct)
    {
        var window = _services.GetRequiredService<ChangeMasterPasswordWindow>();
        var vm = _services.GetRequiredService<ChangeMasterPasswordViewModel>();
        window.DataContext = vm;
        vm.Completed += (_, _) => window.Close();
        vm.CancelRequested += (_, _) => window.Close();

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

        return vm.Changed;
    }
}
