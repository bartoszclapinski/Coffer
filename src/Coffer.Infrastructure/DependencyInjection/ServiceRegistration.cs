using Coffer.Core.Accounts;
using Coffer.Core.Categorization;
using Coffer.Core.Import;
using Coffer.Core.Parsing;
using Coffer.Core.Security;
using Coffer.Core.Transactions;
using Coffer.Infrastructure.Accounts;
using Coffer.Infrastructure.Categorization;
using Coffer.Infrastructure.Import;
using Coffer.Infrastructure.Logging;
using Coffer.Infrastructure.Parsing;
using Coffer.Infrastructure.Parsing.Pko;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Persistence.Encryption;
using Coffer.Infrastructure.Security;
using Coffer.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.DependencyInjection;

public static class ServiceRegistration
{
    public static IServiceCollection AddCofferInfrastructure(this IServiceCollection services) =>
        services
            .AddCofferLogging()
            .AddCofferVaultPaths()
            .AddCofferKeyVault()
            .AddCofferCrypto()
            .AddCofferSetup()
            .AddCofferLogin()
            .AddCofferAutoLock()
            .AddCofferParsing()
            .AddCofferCategorization()
            .AddCofferImport();

    /// <summary>
    /// Registers the headless import pipeline and the transactions read query.
    /// Both depend on <see cref="IDbContextFactory{TContext}"/> from
    /// <see cref="AddCofferDatabase"/>, which the bootstrap registers once the DEK
    /// is available.
    /// </summary>
    public static IServiceCollection AddCofferImport(this IServiceCollection services)
    {
        services.AddTransient<IImportStatementUseCase, ImportStatementUseCase>();
        services.AddTransient<IGetTransactionsQuery, GetTransactionsQuery>();
        services.AddTransient<IAccountService, AccountService>();
        return services;
    }

    /// <summary>
    /// Registers the deterministic categorisation core (Phase 10-A): the pure rule
    /// engine, the learned-cache store, the cache→rules categoriser wired into import,
    /// the UI-facing category service, and the idempotent default seed. Phase 10-C swaps
    /// <see cref="ICategorizer"/> for a hybrid that adds an AI batch.
    /// </summary>
    public static IServiceCollection AddCofferCategorization(this IServiceCollection services)
    {
        services.AddSingleton<ICategoryRuleEngine, RuleEngine>();
        services.AddTransient<ICategoryCacheStore, CategoryCacheStore>();
        services.AddTransient<ICategorizer, RuleCacheCategorizer>();
        services.AddTransient<ICategoryService, CategoryService>();
        services.AddTransient<ICategorySeed, DefaultCategorySeed>();
        return services;
    }

    /// <summary>
    /// Registers the parsing primitives: the format-aware bank detector, every
    /// concrete <see cref="IStatementParser"/>, and the registry that resolves
    /// one parser per (detected bank, format). A later sprint swaps the registry's
    /// "unknown bank → throw" path for an AI-assisted parser without callsite changes.
    /// </summary>
    public static IServiceCollection AddCofferParsing(this IServiceCollection services)
    {
        services.AddSingleton<IBankDetector, FingerprintBankDetector>();
        services.AddSingleton<IStatementParser, PkoHistoriaCsvParser>();
        services.AddSingleton<StatementParserRegistry>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="IVaultPaths"/> as a Singleton wrapping the production
    /// <see cref="CofferVaultPaths"/>. Tests inject their own <see cref="IVaultPaths"/>
    /// pointing at a temp directory before resolving services from the container.
    /// </summary>
    public static IServiceCollection AddCofferVaultPaths(this IServiceCollection services)
    {
        services.AddSingleton<IVaultPaths, CofferVaultPaths>();
        return services;
    }

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
                return new WindowsDpapiKeyVault(sp.GetRequiredService<IVaultPaths>());
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
    /// Registers the Sprint-6 login orchestrator. <see cref="ILoginService"/>
    /// is transient so each call gets a fresh logger scope; the underlying
    /// <see cref="IDekHolder"/> and <see cref="IKeyVault"/> stay singletons.
    /// </summary>
    public static IServiceCollection AddCofferLogin(this IServiceCollection services)
    {
        services.AddTransient<ILoginService, LoginService>();
        return services;
    }

    /// <summary>
    /// Registers the Sprint-6 auto-lock primitives. <see cref="ILastActivityTracker"/>
    /// is a singleton so every UI input registers against the same instance the
    /// monitor polls. <see cref="IAutoLockMonitor"/> is a singleton reused across
    /// the logged-in / logged-out lifecycle by <c>App.axaml.cs</c>. The default
    /// 15-minute timeout is exposed as <see cref="AutoLockOptions"/>; Sprint 7+'s
    /// Settings UI replaces the registration with a configurable source.
    /// </summary>
    public static IServiceCollection AddCofferAutoLock(this IServiceCollection services)
    {
        services.AddSingleton(AutoLockOptions.Default);
        services.AddSingleton<ILastActivityTracker, LastActivityTracker>();
        services.AddSingleton<IAutoLockMonitor, AutoLockMonitor>();
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
            var dbPath = sp.GetRequiredService<IVaultPaths>().DatabaseFile;
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            opts.UseSqlite($"Data Source={dbPath};")
                .AddInterceptors(new SqlCipherKeyInterceptor(dek));
        });

        services.AddTransient<MigrationRunner>();
        services.AddSingleton<IPreMigrationBackup, PreMigrationBackup>();

        return services;
    }
}
