using Microsoft.Extensions.DependencyInjection;

namespace Coffer.Application.DependencyInjection;

public static class ServiceRegistration
{
    public static IServiceCollection AddCofferApplication(this IServiceCollection services) => services;
}
