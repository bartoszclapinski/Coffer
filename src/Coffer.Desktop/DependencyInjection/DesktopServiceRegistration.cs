using Coffer.Application.ViewModels.Setup;
using Coffer.Core.Security;
using Coffer.Desktop.Platform;
using Coffer.Desktop.Views.Setup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coffer.Desktop.DependencyInjection;

public static class DesktopServiceRegistration
{
    public static IServiceCollection AddCofferDesktopUi(this IServiceCollection services)
    {
        services.AddSingleton<MainWindow>();

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
