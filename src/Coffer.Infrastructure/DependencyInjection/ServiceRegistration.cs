using Coffer.Core.Security;
using Coffer.Infrastructure.Logging;
using Coffer.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.DependencyInjection;

public static class ServiceRegistration
{
    public static IServiceCollection AddCofferInfrastructure(this IServiceCollection services) =>
        services
            .AddCofferLogging()
            .AddCofferKeyVault()
            .AddCofferCrypto();

    public static IServiceCollection AddCofferCrypto(this IServiceCollection services)
    {
        services.AddSingleton<IMasterKeyDerivation, Argon2KeyDerivation>();
        services.AddSingleton<ISeedManager, Bip39SeedManager>();
        return services;
    }

    public static IServiceCollection AddCofferKeyVault(this IServiceCollection services)
    {
        services.AddSingleton<IKeyVault>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsDpapiKeyVault();
            }

            var logger = sp.GetRequiredService<ILogger<InMemoryKeyVault>>();
            logger.LogInformation(
                "InMemoryKeyVault selected — non-Windows host, master key cache will not persist across process restarts.");
            return new InMemoryKeyVault();
        });
        return services;
    }
}
