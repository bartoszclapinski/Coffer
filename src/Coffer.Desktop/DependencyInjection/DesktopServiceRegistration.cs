using Coffer.Application.ViewModels.Login;
using Coffer.Application.ViewModels.Main;
using Coffer.Application.ViewModels.Setup;
using Coffer.Core.Security;
using Coffer.Desktop.Platform;
using Coffer.Desktop.Views.Login;
using Coffer.Desktop.Views.Setup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coffer.Desktop.DependencyInjection;

public static class DesktopServiceRegistration
{
    public static IServiceCollection AddCofferDesktopUi(this IServiceCollection services)
    {
        // MainWindow is Transient — App rebuilds it after every logout so the
        // top-level event subscriptions and the VM state start fresh. Sprint 1's
        // Singleton registration would have reused a torn-down window.
        services.AddTransient<MainWindow>();
        services.AddTransient<MainViewModel>();

        services.AddTransient<LoginWindow>();
        services.AddTransient<LoginViewModel>();

        services.AddTransient<SetupWizardViewModel>();
        services.AddTransient<SetupWizardWindow>();

        services.AddSingleton<IScreenCaptureBlocker>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsScreenCaptureBlocker();
            }

            var logger = sp.GetRequiredService<ILogger<NoOpScreenCaptureBlocker>>();
            return new NoOpScreenCaptureBlocker(logger);
        });

        return services;
    }
}
