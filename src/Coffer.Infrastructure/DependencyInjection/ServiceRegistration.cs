using Coffer.Core.Security;
using Coffer.Infrastructure.Logging;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Persistence.Encryption;
using Coffer.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// Registers <see cref="CofferDbContext"/> via a pooled factory and the
    /// <see cref="MigrationRunner"/>. The DEK is resolved lazily through the supplied
    /// provider every time the factory creates a context, so the caller (Sprint 5 setup
    /// wizard or Sprint 6 login flow) decides when and how the DEK becomes available.
    /// Not invoked from <see cref="AddCofferInfrastructure"/> automatically.
    /// </summary>
    public static IServiceCollection AddCofferDatabase(
        this IServiceCollection services,
        Func<IServiceProvider, byte[]> dekProvider)
    {
        ArgumentNullException.ThrowIfNull(dekProvider);

        services.AddDbContextFactory<CofferDbContext>((sp, opts) =>
        {
            var dek = dekProvider(sp);
            var dbPath = CofferPaths.DatabaseFile();
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            opts.UseSqlite($"Data Source={dbPath};")
                .AddInterceptors(new SqlCipherKeyInterceptor(dek));
        });

        services.AddTransient<MigrationRunner>();

        return services;
    }
}
