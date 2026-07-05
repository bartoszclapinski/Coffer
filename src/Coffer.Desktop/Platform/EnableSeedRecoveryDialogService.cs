using Avalonia.Controls.ApplicationLifetimes;
using Coffer.Application.Dialogs;
using Coffer.Application.ViewModels.Recovery;
using Coffer.Core.Security;
using Coffer.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Coffer.Desktop.Platform;

/// <summary>
/// Avalonia implementation of <see cref="IEnableSeedRecoveryDialog"/>. Resolves the
/// <see cref="EnableSeedRecoveryWindow"/> and its view-model, applies screen-capture protection to the
/// window once it opens (the seed is displayed inside), and shows it modally over the main window. Keeps
/// Avalonia window types inside Desktop so the Settings VM stays framework-free (hard rule #4).
/// </summary>
public sealed class EnableSeedRecoveryDialogService : IEnableSeedRecoveryDialog
{
    private readonly IServiceProvider _services;

    public EnableSeedRecoveryDialogService(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public async Task<bool> ShowAsync(CancellationToken ct)
    {
        var window = _services.GetRequiredService<EnableSeedRecoveryWindow>();
        var vm = _services.GetRequiredService<EnableSeedRecoveryViewModel>();
        window.DataContext = vm;
        vm.Completed += (_, _) => window.Close();
        vm.CancelRequested += (_, _) => window.Close();

        // The seed is shown in this window — block screen capture once it has a platform handle.
        var blocker = _services.GetService<IScreenCaptureBlocker>();
        window.Opened += (_, _) =>
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (hwnd != nint.Zero)
            {
                blocker?.Apply(hwnd);
            }
        };

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

        return vm.Enabled;
    }
}
