using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Coffer.Application.ViewModels.Login;
using Coffer.Application.ViewModels.Main;
using Coffer.Application.ViewModels.Setup;
using Coffer.Core.Security;
using Coffer.Desktop.Views.Login;
using Coffer.Desktop.Views.Setup;
using Coffer.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Coffer.Desktop;

public partial class App : Avalonia.Application
{
    public static IServiceProvider Services { get; set; } = null!;

    private IAutoLockMonitor? _autoLockMonitor;
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.MainWindow = ResolveStartupWindow(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private Window ResolveStartupWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var vaultPaths = Services.GetRequiredService<IVaultPaths>();
        var dekFilePath = vaultPaths.EncryptedDekFilePath;
        var databasePath = vaultPaths.DatabaseFile;
        var dekExists = File.Exists(dekFilePath);
        var dbExists = File.Exists(databasePath);

        if (dekExists && dbExists)
        {
            // Silent cold-start path: if the DPAPI cache still holds a valid master
            // key, unlock the vault without any UI. The sync .GetResult() blocks the
            // bootstrap by ~10-30 ms (DPAPI unprotect + AES-GCM decrypt of ~60 B) —
            // tolerable for the bootstrap; revisit with a splash screen if the
            // perceptible delay ever grows.
            var loginService = Services.GetRequiredService<ILoginService>();
            var cachedLoginSucceeded = loginService
                .TryLoginFromCachedKeyAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (cachedLoginSucceeded)
            {
                Log.Information("Cold start unlocked via cached master key; showing MainWindow");
                return BuildMainWindow(desktop);
            }

            Log.Information("Cached master key unavailable; showing LoginWindow");
            return BuildLoginWindow(desktop);
        }

        if (dekExists != dbExists)
        {
            Log.Warning(
                "Partial vault state — dek.encrypted exists: {DekExists}, coffer.db exists: {DbExists}",
                dekExists,
                dbExists);
            return BuildPartialStateError(dekExists, dbExists);
        }

        Log.Information("No vault file detected; starting setup wizard");
        var wizardWindow = Services.GetRequiredService<SetupWizardWindow>();
        var wizardVm = Services.GetRequiredService<SetupWizardViewModel>();
        wizardWindow.DataContext = wizardVm;
        wizardVm.SetupCompleted += (_, args) => OnSetupCompleted(desktop, wizardWindow, args);
        return wizardWindow;
    }

    private Window BuildLoginWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = Services.GetRequiredService<LoginWindow>();
        var vm = Services.GetRequiredService<LoginViewModel>();
        window.DataContext = vm;
        vm.LoginCompleted += (_, _) => OnLoginCompleted(desktop, window);
        return window;
    }

    private void OnLoginCompleted(IClassicDesktopStyleApplicationLifetime desktop, Window loginWindow)
    {
        Log.Information("Login completed; swapping to MainWindow");
        var mainWindow = BuildMainWindow(desktop);
        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        loginWindow.Close();
    }

    private Window BuildMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = Services.GetRequiredService<MainWindow>();
        var vm = Services.GetRequiredService<MainViewModel>();
        window.DataContext = vm;
        vm.LoggedOut += (_, _) => HandleLogout(desktop, window);

        StartAutoLockMonitor(desktop);
        return window;
    }

    private void StartAutoLockMonitor(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var monitor = Services.GetRequiredService<IAutoLockMonitor>();
        var options = Services.GetRequiredService<AutoLockOptions>();

        // Detach any previous subscription before re-subscribing — the monitor is a
        // singleton that survives logout / login cycles.
        if (_autoLockMonitor is not null)
        {
            _autoLockMonitor.AutoLockTriggered -= OnAutoLockTriggered;
        }

        _autoLockMonitor = monitor;
        monitor.AutoLockTriggered += OnAutoLockTriggered;
        monitor.Start(options.IdleTimeout);
    }

    private void OnAutoLockTriggered(object? sender, EventArgs e)
    {
        // Monitor raises on a thread-pool thread; UI swap must run on the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            if (_desktop?.MainWindow is { } window)
            {
                Log.Information("Auto-lock triggered; locking session");
                HandleLogout(_desktop, window);
            }
        });
    }

    private void HandleLogout(IClassicDesktopStyleApplicationLifetime desktop, Window currentWindow)
    {
        // Single source of truth for both manual logout (Wyloguj) and auto-lock —
        // every path goes through here so the two cannot drift apart.
        if (_autoLockMonitor is not null)
        {
            _autoLockMonitor.AutoLockTriggered -= OnAutoLockTriggered;
            _autoLockMonitor.Stop();
        }

        var loginService = Services.GetRequiredService<ILoginService>();
        // Fire-and-forget: LoginService.LogoutAsync is internally defensive; the
        // window swap should not wait on it because cache invalidation can block
        // briefly on disk I/O. Errors are logged inside the service.
        _ = loginService.LogoutAsync(CancellationToken.None);

        var loginWindow = BuildLoginWindow(desktop);
        desktop.MainWindow = loginWindow;
        loginWindow.Show();
        currentWindow.Close();
    }

    private void OnSetupCompleted(
        IClassicDesktopStyleApplicationLifetime desktop,
        Window wizardWindow,
        SetupCompletedEventArgs args)
    {
        if (!args.Success)
        {
            // Wizard stays open; ConfirmStepViewModel.ErrorMessage shows the failure
            // and the user can retry. SetupService already rolled back any persisted state.
            return;
        }

        Log.Information("Setup wizard completed successfully; swapping to MainWindow");

        var mainWindow = BuildMainWindow(desktop);
        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        wizardWindow.Close();
    }

    private static Window BuildPartialStateError(bool dekExists, bool dbExists)
    {
        var present = dekExists ? "dek.encrypted" : "coffer.db";
        var missing = dekExists ? "coffer.db" : "dek.encrypted";
        var folder = Services.GetRequiredService<IVaultPaths>().LocalAppDataFolder;
        return BuildSimpleMessageWindow(
            title: "Coffer — niedopasowany stan sejfu",
            message:
                $"Wykryto niedopasowany stan sejfu w {folder}\\: " +
                $"{present} istnieje, ale {missing} brakuje.\n\n" +
                "Sejf wymaga obu plików razem. Aby zacząć od nowa, usuń oba pliki " +
                "(razem z coffer.db-wal i coffer.db-shm jeśli istnieją) " +
                "i uruchom aplikację ponownie. Aby zalogować się do istniejącego " +
                "sejfu, przywróć brakujący plik z kopii zapasowej.");
    }

    private static Window BuildSimpleMessageWindow(string title, string message)
    {
        return new Window
        {
            Title = title,
            Width = 640,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Thickness(24),
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }
}
