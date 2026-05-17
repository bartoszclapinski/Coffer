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
            .AddCofferCrypto()
            .AddCofferSetup();

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
    /// Registers the Sprint-5 setup primitives: the in-memory <see cref="IDekHolder"/>
    /// bridge, the zxcvbn password-strength checker, and the <see cref="ISetupService"/>
    /// orchestrator.
    /// </summary>
    public static IServiceCollection AddCofferSetup(this IServiceCollection services)
    {
        services.AddSingleton<IDekHolder, DekHolder>();
        services.AddSingleton<IPasswordStrengthChecker, ZxcvbnPasswordStrengthChecker>();
        services.AddTransient<Func<IDbContextFactory<CofferDbContext>>>(sp =>
            () => sp.GetRequiredService<IDbContextFactory<CofferDbContext>>());
        services.AddTransient<ISetupService, SetupService>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="CofferDbContext"/> via a pooled factory and the
    /// <see cref="MigrationRunner"/>. If <paramref name="dekProvider"/> is omitted,
    /// the DEK is resolved through <see cref="IDekHolder.Get"/> on first context
    /// creation — the Sprint 5 setup wizard and the Sprint 6 login flow publish the
    /// DEK there. Callers may override the provider explicitly (tests, alternate
    /// boot paths).
    /// </summary>
    /// <param name="dekProvider">
    /// Optional. <see cref="DbContextFactoryExtensions"/> builds
    /// <see cref="DbContextOptions{TContext}"/> exactly once, lazily, when the first
    /// <see cref="CofferDbContext"/> is created from the registered factory. The
    /// returned byte array is captured by <see cref="Persistence.Encryption.SqlCipherKeyInterceptor"/>
    /// for the lifetime of the DI container; rotating the DEK requires building a new
    /// container.
    /// </param>
    public static IServiceCollection AddCofferDatabase(
        this IServiceCollection services,
        Func<IServiceProvider, byte[]>? dekProvider = null)
    {
        var effectiveProvider = dekProvider
            ?? (sp => sp.GetRequiredService<IDekHolder>().Get());

        services.AddDbContextFactory<CofferDbContext>((sp, opts) =>
        {
            var dek = effectiveProvider(sp);
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
