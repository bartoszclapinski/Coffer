using Coffer.Application.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Coffer.Application.DependencyInjection;

public static class ServiceRegistration
{
    public static IServiceCollection AddCofferApplication(this IServiceCollection services)
    {
        // Singleton: every view binds to the same localizer through the {l:Localize}
        // markup extension, so one SetLanguage call re-labels the whole UI at once.
        services.AddSingleton<ILocalizer, Localizer>();
        return services;
    }
}
