using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Coffer.Application.Localization;
using Coffer.Application.ViewModels.Login;
using Coffer.Application.ViewModels.Main;
using Coffer.Application.ViewModels.Recovery;
using Coffer.Application.ViewModels.Setup;
using Coffer.Core.Backup;
using Coffer.Core.Categorization;
using Coffer.Core.Goals;
using Coffer.Core.Localization;
using Coffer.Core.Security;
using Coffer.Core.Theming;
using Coffer.Desktop.Theme;
using Coffer.Desktop.Views;
using Coffer.Desktop.Views.Login;
using Coffer.Desktop.Views.Setup;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Coffer.Desktop;

public partial class App : Avalonia.Application
{
    public static IServiceProvider Services { get; set; } = null!;

    private IAutoLockMonitor? _autoLockMonitor;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private bool _logoutInFlight;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ApplySavedTheme();
        ApplySavedLanguage();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.MainWindow = ResolveStartupWindow(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ApplySavedTheme()
    {
        // Apply the persisted UI theme before any window is built so the first screen
        // (setup/login/main) renders in the chosen variant. The store reads a plaintext
        // file and never throws; a failure here must not block startup.
        try
        {
            var themeStore = Services.GetRequiredService<IThemeStore>();
            ThemeManager.Apply(themeStore.Load());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to apply saved UI theme; continuing with the default");
        }
    }

    private static void ApplySavedLanguage()
    {
        // Apply the persisted UI language before any window is built so the first screen
        // (setup/login/main) renders in the chosen language. The store reads a plaintext
        // file and never throws; a failure here must not block startup.
        try
        {
            var localizer = Services.GetRequiredService<ILocalizer>();
            var languageStore = Services.GetRequiredService<ILanguageStore>();
            localizer.SetLanguage(languageStore.Load());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to apply saved UI language; continuing with the default");
        }
    }

    private Window ResolveStartupWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Dev-only: launch the design-system style gallery instead of the app (Sprint 28-A
        // validation surface). Guarded by an environment variable; no effect in normal use.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COFFER_STYLE_GALLERY")))
        {
            return new StyleGalleryWindow();
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COFFER_OVERVIEW_PREVIEW")))
        {
            return new OverviewPreviewWindow();
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COFFER_TX_PREVIEW")))
        {
            return new TransactionsPreviewWindow();
        }

        // Apply any restore staged from Settings BEFORE touching the database — the swap must happen while
        // the file is closed. A failure (e.g. the staged snapshot vanished) returns a recovery window.
        if (ApplyPendingRestore(desktop) is { } restoreError)
        {
            return restoreError;
        }

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
                Log.Information("Cold start unlocked via cached master key");
                return BuildPostUnlockWindow(desktop);
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
            return BuildPartialStateError(dekExists, dbExists, vaultPaths.LocalAppDataFolder);
        }

        Log.Information("No vault file detected; starting setup wizard");
        var wizardWindow = Services.GetRequiredService<SetupWizardWindow>();
        var wizardVm = Services.GetRequiredService<SetupWizardViewModel>();
        wizardWindow.DataContext = wizardVm;
        // The seed-display step view raises a bubbling event when it is shown; resolve the
        // platform blocker here (composition root) so the view stays free of DI lookups.
        wizardWindow.AddHandler(BipSeedDisplayStepView.SeedDisplayReadyEvent, OnSeedDisplayReady);
        wizardVm.SetupCompleted += (_, args) => OnSetupCompleted(desktop, wizardWindow, args);
        return wizardWindow;
    }

    /// <summary>
    /// Applies a restore staged from Settings (doc 08). Runs before the database is opened so the swap
    /// happens against a closed file. Returns <c>null</c> when nothing is staged or the restore applied
    /// cleanly (startup then continues normally against the restored database); returns a recovery window
    /// when the staged snapshot can no longer be applied, so the failure is visible rather than swallowed.
    /// </summary>
    private Window? ApplyPendingRestore(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var restoreService = Services.GetRequiredService<IRestoreService>();

        PendingRestore? pending;
        try
        {
            pending = restoreService.GetPendingRestoreAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read the restore marker; continuing without restoring");
            return null;
        }

        if (pending is null)
        {
            return null;
        }

        try
        {
            var result = restoreService
                .ApplyPendingRestoreAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (result.Applied)
            {
                Log.Information(
                    "Applied pending restore from snapshot {Date}; safety copy at {Path}",
                    result.RestoredFrom,
                    result.SafetyCopyPath);
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Pending restore failed to apply at startup");
            return BuildRestoreFailedWindow(desktop);
        }
    }

    private Window BuildRestoreFailedWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var localizer = Services.GetRequiredService<ILocalizer>();

        var message = new TextBlock
        {
            Text = localizer["App.Restore.Failed.Message"],
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };

        var continueButton = new Button { Content = localizer["App.Restore.Failed.Continue"], IsDefault = true };
        var quitButton = new Button { Content = localizer["App.Restore.Failed.Quit"], IsCancel = true };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { quitButton, continueButton },
        };

        var window = new Window
        {
            Title = localizer["App.Restore.Failed.Title"],
            Width = 560,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children = { message, buttons },
            },
        };

        quitButton.Click += (_, _) =>
        {
            Log.Information("User quit after a failed restore");
            desktop.Shutdown();
        };

        continueButton.Click += (_, _) =>
        {
            // Discard the unappliable restore request and continue into the normal startup path.
            try
            {
                Services.GetRequiredService<IRestoreService>()
                    .CancelPendingRestoreAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not clear the restore marker; continuing anyway");
            }

            var next = ResolveStartupWindow(desktop);
            desktop.MainWindow = next;
            next.Show();
            window.Close();
        };

        return window;
    }

    private Window BuildLoginWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = Services.GetRequiredService<LoginWindow>();
        var vm = Services.GetRequiredService<LoginViewModel>();
        window.DataContext = vm;
        vm.LoginCompleted += (_, _) => OnLoginCompleted(desktop, window);
        vm.RestoreFromSeedRequested += (_, _) => OnRestoreFromSeedRequested(desktop, window);
        return window;
    }

    private async void OnRestoreFromSeedRequested(
        IClassicDesktopStyleApplicationLifetime desktop, Window loginWindow)
    {
        var window = Services.GetRequiredService<RestoreFromSeedWindow>();
        var vm = Services.GetRequiredService<RestoreFromSeedViewModel>();
        window.DataContext = vm;

        Window? recovered = null;
        vm.RecoveryCompleted += (_, _) =>
        {
            // Recovery reset the password and published the DEK — route in like a normal login.
            recovered = BuildPostUnlockWindow(desktop);
            window.Close();
        };

        try
        {
            await window.ShowDialog(loginWindow);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Restore-from-seed window failed");
            return;
        }

        if (recovered is not null)
        {
            desktop.MainWindow = recovered;
            recovered.Show();
            loginWindow.Close();
        }
    }

    private void OnLoginCompleted(IClassicDesktopStyleApplicationLifetime desktop, Window loginWindow)
    {
        Log.Information("Login completed; resolving post-unlock window");
        var nextWindow = BuildPostUnlockWindow(desktop);
        desktop.MainWindow = nextWindow;
        nextWindow.Show();
        loginWindow.Close();
    }

    /// <summary>
    /// After the vault is unlocked (cached login, password login, or fresh setup),
    /// apply any pending schema migrations before showing the main UI. Per doc 02 the
    /// user must confirm and a pre-migration backup is mandatory (hard rule #8); if
    /// migrations are pending we route to the confirm window first, otherwise straight
    /// to <see cref="MainWindow"/>.
    /// </summary>
    private Window BuildPostUnlockWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (HasPendingMigrations())
        {
            Log.Information("Pending database migrations detected; showing migration confirmation");
            return BuildMigrationConfirmWindow(desktop);
        }

        return BuildMainWindow(desktop);
    }

    private static bool HasPendingMigrations()
    {
        var factory = Services.GetRequiredService<IDbContextFactory<CofferDbContext>>();
        using var db = factory.CreateDbContext();
        return db.Database.GetPendingMigrations().Any();
    }

    private Window BuildMigrationConfirmWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var localizer = Services.GetRequiredService<ILocalizer>();

        var message = new TextBlock
        {
            Text = localizer["App.Migration.Message"],
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };

        var status = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            IsVisible = false,
        };

        var continueButton = new Button { Content = localizer["App.Migration.Continue"], IsDefault = true };
        var cancelButton = new Button { Content = localizer["App.Migration.Quit"], IsCancel = true };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancelButton, continueButton },
        };

        var window = new Window
        {
            Title = localizer["App.Migration.Title"],
            Width = 560,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children = { message, status, buttons },
            },
        };

        cancelButton.Click += (_, _) =>
        {
            Log.Information("User cancelled the database migration; shutting down");
            desktop.Shutdown();
        };

        continueButton.Click += async (_, _) =>
        {
            continueButton.IsEnabled = false;
            cancelButton.IsEnabled = false;
            status.IsVisible = true;
            status.Text = localizer["App.Migration.Progress"];

            try
            {
                await RunStartupMigrationAsync(CancellationToken.None);
                var mainWindow = BuildMainWindow(desktop);
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                window.Close();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Database migration failed at startup");
                status.Text = localizer["App.Migration.Failed"];
                cancelButton.IsEnabled = true;
            }
        };

        return window;
    }

    private static async Task RunStartupMigrationAsync(CancellationToken ct)
    {
        var factory = Services.GetRequiredService<IDbContextFactory<CofferDbContext>>();
        var backup = Services.GetRequiredService<IPreMigrationBackup>();
        var logger = Services.GetRequiredService<ILogger<MigrationRunner>>();

        await using var db = await factory.CreateDbContextAsync(ct);
        var runner = new MigrationRunner(db, logger, ct2 => backup.CreateSnapshotAsync(ct2));
        var result = await runner.RunPendingMigrationsAsync(ct);

        if (result.Status == MigrationStatus.Failed)
        {
            throw new InvalidOperationException(
                $"Startup migration did not complete: {result.Status}");
        }
    }

    private Window BuildMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        SeedDefaultCategories();
        StartDailyAdvisorRefresh();
        StartDailyBackup();

        var window = Services.GetRequiredService<MainWindow>();
        var vm = Services.GetRequiredService<MainViewModel>();
        window.DataContext = vm;
        vm.LoggedOut += (_, _) => HandleLogout(desktop, window);

        StartAutoLockMonitor(desktop);
        return window;
    }

    private static void StartDailyAdvisorRefresh()
    {
        // Once-a-day goal snapshots + advisor report (doc 07). Idempotent within a day, so firing on
        // every launch is safe. Runs off the bootstrap thread because it may make an LLM call; a
        // failure here must never block the user from the app.
        _ = Task.Run(async () =>
        {
            try
            {
                var job = Services.GetRequiredService<IGoalSnapshotJob>();
                await job.RunAsync(DateOnly.FromDateTime(DateTime.Now), CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Daily advisor refresh failed; continuing without today's report");
            }
        });
    }

    private static void StartDailyBackup()
    {
        // Daily local snapshot of the encrypted database (doc 08, Layer 1). Idempotent within a day, so
        // firing on every launch is safe. Runs off the bootstrap thread after a short delay so it does not
        // compete with startup I/O; a failure here must never crash or block the app.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), CancellationToken.None);
                var backup = Services.GetRequiredService<IBackupService>();
                await backup.CreateDailySnapshotAsync(DateOnly.FromDateTime(DateTime.Now), CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Daily backup snapshot failed; continuing without today's snapshot");
            }
        });
    }

    private static void SeedDefaultCategories()
    {
        // Idempotent: a no-op once seeded (two COUNT checks). Runs on the bootstrap
        // thread before MainWindow is shown — the insert is tiny and only happens once.
        try
        {
            var seed = Services.GetRequiredService<ICategorySeed>();
            seed.SeedAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Seeding is best-effort: a failure here must not block the user from the app.
            // They can still import; categories simply will not be pre-populated.
            Log.Warning(ex, "Default category seed failed; continuing without it");
        }
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
        // every path goes through here so the two cannot drift apart. Guard against
        // concurrent invocation: a Wyloguj click landing in the same dispatcher tick
        // as an auto-lock fire would otherwise build two LoginWindows. Both paths run
        // only on the UI thread, so a plain flag suffices — no lock needed.
        if (_logoutInFlight)
        {
            return;
        }

        _logoutInFlight = true;
        try
        {
            if (_autoLockMonitor is not null)
            {
                _autoLockMonitor.AutoLockTriggered -= OnAutoLockTriggered;
                _autoLockMonitor.Stop();
            }

            var loginService = Services.GetRequiredService<ILoginService>();
            // Invalidate the cache synchronously (same pattern as ResolveStartupWindow's
            // cached-login call): a fire-and-forget can be killed by an immediate app exit,
            // leaving the DPAPI cache on disk after an explicit logout. The invalidation is
            // sub-millisecond local I/O, so blocking the UI thread is imperceptible.
            try
            {
                loginService.LogoutAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Never trap the user on a locked screen because logout I/O failed.
                Log.Warning(ex, "LogoutAsync failed; navigating to the login window anyway");
            }

            var loginWindow = BuildLoginWindow(desktop);
            desktop.MainWindow = loginWindow;
            loginWindow.Show();
            currentWindow.Close();
        }
        finally
        {
            _logoutInFlight = false;
        }
    }

    private static void OnSeedDisplayReady(object? sender, SeedDisplayReadyEventArgs e)
    {
        // Best-effort: a missing blocker (non-Windows no-op) or an Apply failure must never
        // block the setup wizard — screen-capture protection is a hardening nicety, not a gate.
        var blocker = Services.GetService<IScreenCaptureBlocker>();
        blocker?.Apply(e.WindowHandle);
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

        Log.Information("Setup wizard completed successfully; resolving post-unlock window");

        var nextWindow = BuildPostUnlockWindow(desktop);
        desktop.MainWindow = nextWindow;
        nextWindow.Show();
        wizardWindow.Close();
    }

    private static Window BuildPartialStateError(bool dekExists, bool dbExists, string folder)
    {
        var localizer = Services.GetRequiredService<ILocalizer>();
        var present = dekExists ? "dek.encrypted" : "coffer.db";
        var missing = dekExists ? "coffer.db" : "dek.encrypted";
        return BuildSimpleMessageWindow(
            title: localizer["App.PartialVault.Title"],
            message: localizer.Format("App.PartialVault.Message", folder, present, missing));
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
