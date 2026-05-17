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
        if (File.Exists(dekFilePath))
        {
            Log.Information("Vault detected at {Path}; showing Sprint 6 placeholder", dekFilePath);
            return BuildSprint6Placeholder();
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
        return new Window
        {
            Title = "Coffer",
            Width = 640,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Content = new TextBlock
            {
                Text = "Sejf już istnieje. Logowanie pojawi się w Sprint 6 — usuń " +
                       "%LocalAppData%\\Coffer\\dek.encrypted (i opcjonalnie coffer.db) " +
                       "jeśli chcesz przetestować ponowny setup. Niedopasowany stan " +
                       "(np. dek.encrypted istnieje, ale coffer.db brakuje) wymaga " +
                       "manual cleanup obu plików — Sprint 6 doda automatyczną detekcję.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Thickness(24),
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }
}
