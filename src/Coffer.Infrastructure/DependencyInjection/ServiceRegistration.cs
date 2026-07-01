using Coffer.Core.Accounts;
using Coffer.Core.Ai;
using Coffer.Core.Anomalies;
using Coffer.Core.Categorization;
using Coffer.Core.Chat;
using Coffer.Core.Dashboard;
using Coffer.Core.Goals;
using Coffer.Core.Import;
using Coffer.Core.Localization;
using Coffer.Core.Parsing;
using Coffer.Core.Planning;
using Coffer.Core.Security;
using Coffer.Core.Transactions;
using Coffer.Infrastructure.Accounts;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Anomalies;
using Coffer.Infrastructure.Anomalies.Detectors;
using Coffer.Infrastructure.Categorization;
using Coffer.Infrastructure.Chat;
using Coffer.Infrastructure.Dashboard;
using Coffer.Infrastructure.Goals;
using Coffer.Infrastructure.Goals.Strategies;
using Coffer.Infrastructure.Import;
using Coffer.Infrastructure.Localization;
using Coffer.Infrastructure.Logging;
using Coffer.Infrastructure.Parsing;
using Coffer.Infrastructure.Parsing.Ai;
using Coffer.Infrastructure.Parsing.Pko;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Persistence.Encryption;
using Coffer.Infrastructure.Planning;
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
            .AddCofferAi()
            .AddCofferChat()
            .AddCofferImport()
            .AddCofferAnomalies()
            .AddCofferGoals()
            .AddCofferPlanning();

    /// <summary>
    /// Registers the Sprint-16 cash-flow planning spine (beyond roadmap): the persisted
    /// <see cref="IRecurringFlowRepository"/>, the <see cref="IRecurringFlowDetector"/> that proposes
    /// flows from history, the <see cref="IRunningBalanceQuery"/> opening balance and its
    /// <see cref="IStatementContinuityChecker"/> guard, and the deterministic
    /// <see cref="CashFlowProjectionEngine"/>. The engine calculates; the 16-C AI only explains.
    /// </summary>
    public static IServiceCollection AddCofferPlanning(this IServiceCollection services)
    {
        services.AddSingleton<CashFlowProjectionEngine>();
        services.AddSingleton<AffordabilityEngine>();
        services.AddTransient<IRecurringFlowRepository, RecurringFlowRepository>();
        services.AddTransient<IRecurringFlowDetector, RecurringFlowDetector>();
        services.AddTransient<IRunningBalanceQuery, RunningBalanceQuery>();
        services.AddTransient<IStatementContinuityChecker, StatementContinuityChecker>();
        services.AddTransient<IBalanceTrustQuery, BalanceTrustQuery>();
        services.AddTransient<IPlanningSettings, AppSettingsStore>();
        services.AddTransient<IVariableBurnQuery, VariableBurnQuery>();
        services.AddTransient<ICashFlowExplainer, CashFlowExplainer>();
        return services;
    }

    /// <summary>
    /// Registers the Phase 9 financial advisor engine (doc 07): one <see cref="GoalStrategy"/> per
    /// <see cref="GoalType"/>, the <see cref="IGoalFeasibilityEngine"/> that dispatches to them and
    /// wires the cross-goal free-cash pull, the <see cref="IMortgagePrepaymentCalculator"/> that
    /// quantifies both shorten/reduce modes without recommending either, and the
    /// <see cref="IFinancialContextBuilder"/> that derives the deterministic context from the
    /// transaction history. Everything here is pure and free — the engine calculates; the 14-C AI
    /// only explains.
    /// </summary>
    public static IServiceCollection AddCofferGoals(this IServiceCollection services)
    {
        services.AddTransient<GoalStrategy, PurchaseGoalStrategy>();
        services.AddTransient<GoalStrategy, LargeExpenseGoalStrategy>();
        services.AddTransient<GoalStrategy, EmergencyFundGoalStrategy>();
        services.AddTransient<GoalStrategy, MortgagePrepaymentGoalStrategy>();
        services.AddTransient<GoalStrategy, InvestmentGoalStrategy>();
        services.AddTransient<GoalStrategy, LongTermGoalStrategy>();
        services.AddTransient<IGoalFeasibilityEngine, GoalFeasibilityEngine>();
        services.AddSingleton<IMortgagePrepaymentCalculator, MortgagePrepaymentCalculator>();
        services.AddTransient<IFinancialContextBuilder, FinancialContextBuilder>();
        services.AddTransient<IGoalsQuery, GoalsQuery>();
        services.AddTransient<IGoalService, GoalService>();
        services.AddTransient<IAdvisorReportQuery, AdvisorReportQuery>();
        services.AddTransient<IGoalSnapshotJob, GoalSnapshotJob>();
        return services;
    }

    /// <summary>
    /// Registers the Phase 8 anomaly engine: the five statistical <see cref="IAnomalyDetector"/>s
    /// (doc 04, "statistics first"), the <see cref="IDetectAnomaliesUseCase"/> that runs them and
    /// upserts <see cref="Core.Domain.Alert"/> rows by signature, and the alert read query and
    /// lifecycle service for the Alerty page. No AI here — detection is deterministic and free;
    /// the 13-B commentator adds optional LLM explanations on top.
    /// </summary>
    public static IServiceCollection AddCofferAnomalies(this IServiceCollection services)
    {
        services.AddTransient<IAnomalyDetector, HighAmountInCategoryDetector>();
        services.AddTransient<IAnomalyDetector, NewMerchantDetector>();
        services.AddTransient<IAnomalyDetector, CategorySpikeDetector>();
        services.AddTransient<IAnomalyDetector, DuplicatePaymentDetector>();
        services.AddTransient<IAnomalyDetector, MissingRecurrenceDetector>();
        services.AddTransient<IDetectAnomaliesUseCase, AnomalyDetectionService>();
        services.AddTransient<IAlertsQuery, AlertsQuery>();
        services.AddTransient<IAlertService, AlertService>();
        return services;
    }

    /// <summary>
    /// Registers the Phase 10-B AI plumbing: the secret store for API keys, the
    /// provider-neutral <see cref="IAiProvider"/> (Claude today), the prompt anonymiser
    /// (hard rule #7), token pricing, the cost ledger and budget gate (doc 04), and the
    /// KV-backed AI settings. Nothing here calls a vendor API at registration time — the
    /// key is resolved per-call. Phase 10-C wires these into the hybrid categoriser.
    /// </summary>
    public static IServiceCollection AddCofferAi(this IServiceCollection services)
    {
        services.AddSingleton<ISecretStore>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsDpapiSecretStore(sp.GetRequiredService<IVaultPaths>());
            }

            var logger = sp.GetRequiredService<ILogger<InMemorySecretStore>>();
            logger.LogInformation(
                "InMemorySecretStore selected — non-Windows host, AI API keys will not persist across process restarts.");
            return new InMemorySecretStore();
        });

        services.AddTransient<IAiProvider, ClaudeProvider>();
        services.AddSingleton<IPromptAnonymizer, PromptAnonymizer>();
        services.AddSingleton<IAiPricing, AiPricing>();
        services.AddTransient<IAiUsageLedger, AiUsageLedger>();
        services.AddTransient<IAiBudgetGate, AiBudgetGate>();
        services.AddTransient<IAiSettings, AppSettingsStore>();
        services.AddTransient<IAnomalyCommentator, AnomalyCommentator>();
        services.AddTransient<IAdvisorReportGenerator, AdvisorReportGenerator>();
        return services;
    }

    /// <summary>
    /// Registers the Phase 7 chat-with-data plumbing: the four read-only financial tools
    /// (<see cref="IChatTool"/>) and the <see cref="IChatService"/> orchestrator that runs the
    /// tool-call loop over <see cref="IAiProvider"/> behind the budget gate and cost ledger. Tools
    /// are read-only — chat can never mutate state. The UI (12-B) consumes <see cref="IChatService"/>.
    /// </summary>
    public static IServiceCollection AddCofferChat(this IServiceCollection services)
    {
        services.AddTransient<IChatTool, GetTotalSpentTool>();
        services.AddTransient<IChatTool, GetTransactionsTool>();
        services.AddTransient<IChatTool, GetSpendingByCategoryTool>();
        services.AddTransient<IChatTool, GetMonthlyTrendTool>();
        services.AddTransient<IChatTool, FindAnomaliesTool>();
        services.AddTransient<IChatTool, GetGoalsTool>();
        services.AddTransient<IChatTool, GetCashFlowProjectionTool>();
        services.AddTransient<IChatTool, CanIAffordTool>();
        services.AddTransient<IChatService, ChatService>();
        return services;
    }

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
        services.AddTransient<IDashboardQuery, DashboardQuery>();
        services.AddTransient<IAccountService, AccountService>();
        return services;
    }

    /// <summary>
    /// Registers the categorisation pipeline: the pure rule engine, the learned-cache
    /// store, the UI-facing category service, and the idempotent default seed. The active
    /// <see cref="ICategorizer"/> is the Phase 10-C <see cref="HybridCategorizer"/>
    /// (cache → rules → AI batch behind the budget gate); the deterministic
    /// <see cref="RuleCacheCategorizer"/> remains the free fallback path inside it.
    /// </summary>
    public static IServiceCollection AddCofferCategorization(this IServiceCollection services)
    {
        services.AddSingleton<ICategoryRuleEngine, RuleEngine>();
        services.AddTransient<ICategoryCacheStore, CategoryCacheStore>();
        services.AddTransient<ICategorizer, HybridCategorizer>();
        services.AddTransient<ICategoryService, CategoryService>();
        services.AddTransient<ICategorySeed, DefaultCategorySeed>();
        return services;
    }

    /// <summary>
    /// Registers the parsing primitives: the format-aware bank detector, every
    /// concrete deterministic <see cref="IStatementParser"/>, the AI-assisted fallback
    /// parser, and the registry that resolves one parser per (detected bank, format) and
    /// falls back to the AI parser for unknown banks (Sprint 17). The fallback is injected
    /// explicitly — registered by its own type, not as an <see cref="IStatementParser"/> — so a
    /// deterministic parser always wins for a known bank+format.
    /// </summary>
    public static IServiceCollection AddCofferParsing(this IServiceCollection services)
    {
        services.AddSingleton<IBankDetector, FingerprintBankDetector>();
        services.AddSingleton<IStatementParser, PkoHistoriaCsvParser>();
        services.AddSingleton<AiAssistedParser>();
        services.AddSingleton(sp => new StatementParserRegistry(
            sp.GetServices<IStatementParser>(),
            sp.GetRequiredService<AiAssistedParser>()));
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
        // Plaintext language preference, readable pre-login (before the DEK exists).
        services.AddSingleton<ILanguageStore, FileLanguageStore>();
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

        // Singleton so the DI container disposes it at shutdown, zeroing the DEK copy
        // the interceptor holds. The DEK is resolved lazily on first context creation,
        // when the options below first resolve this service.
        services.AddSingleton(sp => new SqlCipherKeyInterceptor(effectiveProvider(sp)));

        services.AddDbContextFactory<CofferDbContext>((sp, opts) =>
        {
            var dbPath = sp.GetRequiredService<IVaultPaths>().DatabaseFile;
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            opts.UseSqlite($"Data Source={dbPath};")
                .AddInterceptors(sp.GetRequiredService<SqlCipherKeyInterceptor>());
        });

        services.AddTransient<MigrationRunner>();
        services.AddSingleton<IPreMigrationBackup, PreMigrationBackup>();

        return services;
    }
}
