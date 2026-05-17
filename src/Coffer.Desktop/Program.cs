using Avalonia;
using Coffer.Application.DependencyInjection;
using Coffer.Core.DependencyInjection;
using Coffer.Desktop.DependencyInjection;
using Coffer.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Coffer.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var services = new ServiceCollection()
            .AddCofferCore()
            .AddCofferApplication()
            .AddCofferInfrastructure()
            .AddCofferDatabase()
            .AddCofferDesktopUi();

        App.Services = services.BuildServiceProvider();

        try
        {
            Log.Information("Coffer starting, runtime {Runtime}", Environment.Version);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Coffer terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
