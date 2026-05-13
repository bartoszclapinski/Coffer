using Microsoft.Extensions.DependencyInjection;

namespace Coffer.Desktop.DependencyInjection;

public static class DesktopServiceRegistration
{
    public static IServiceCollection AddCofferDesktopUi(this IServiceCollection services)
    {
        services.AddSingleton<MainWindow>();
        return services;
    }
}
