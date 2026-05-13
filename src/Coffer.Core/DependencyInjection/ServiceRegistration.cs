using Microsoft.Extensions.DependencyInjection;

namespace Coffer.Core.DependencyInjection;

public static class ServiceRegistration
{
    public static IServiceCollection AddCofferCore(this IServiceCollection services) => services;
}
