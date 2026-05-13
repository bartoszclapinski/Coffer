using Coffer.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Coffer.Infrastructure.DependencyInjection;

public static class ServiceRegistration
{
    public static IServiceCollection AddCofferInfrastructure(this IServiceCollection services) =>
        services.AddCofferLogging();
}
