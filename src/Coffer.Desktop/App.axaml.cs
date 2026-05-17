using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Coffer.Application.ViewModels.Setup;
using Coffer.Desktop.Views.Setup;
using Coffer.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Coffer.Desktop;

public partial class App : Avalonia.Application
{
    public static IServiceProvider Services { get; set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = ResolveStartupWindow(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Window ResolveStartupWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var dekFilePath = CofferPaths.EncryptedDekFilePath();
        var databasePath = CofferPaths.DatabaseFile();
        var dekExists = File.Exists(dekFilePath);
        var dbExists = File.Exists(databasePath);

        if (dekExists && dbExists)
        {
            Log.Information("Vault detected at {Path}; showing Sprint 6 placeholder", dekFilePath);
            return BuildSprint6Placeholder();
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

    private static void OnSetupCompleted(
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

        var mainWindow = Services.GetRequiredService<MainWindow>();
        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        wizardWindow.Close();
    }

    private static Window BuildSprint6Placeholder()
    {
        return BuildSimpleMessageWindow(
            title: "Coffer",
            message: "Sejf już istnieje. Logowanie pojawi się w Sprint 6 — usuń " +
                     "%LocalAppData%\\Coffer\\dek.encrypted oraz coffer.db (razem z " +
                     "coffer.db-wal i coffer.db-shm) jeśli chcesz przetestować " +
                     "ponowny setup.");
    }

    private static Window BuildPartialStateError(bool dekExists, bool dbExists)
    {
        var present = dekExists ? "dek.encrypted" : "coffer.db";
        var missing = dekExists ? "coffer.db" : "dek.encrypted";
        return BuildSimpleMessageWindow(
            title: "Coffer — niedopasowany stan sejfu",
            message:
                $"Wykryto niedopasowany stan sejfu w %LocalAppData%\\Coffer\\: " +
                $"{present} istnieje, ale {missing} brakuje.\n\n" +
                "Sejf wymaga obu plików razem. Aby zacząć od nowa, usuń oba pliki " +
                "(razem z coffer.db-wal i coffer.db-shm jeśli istnieją) " +
                "i uruchom aplikację ponownie. Aby zalogować się do istniejącego " +
                "sejfu, przywróć brakujący plik z kopii zapasowej.\n\n" +
                "Sprint 6 doda obsługę tej sytuacji w UI.");
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
