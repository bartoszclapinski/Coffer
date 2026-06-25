using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Persistence;

public sealed class CofferDbContext : DbContext
{
    public CofferDbContext(DbContextOptions<CofferDbContext> options)
        : base(options)
    {
    }

    public DbSet<SchemaInfoEntry> SchemaInfo => Set<SchemaInfoEntry>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<ImportSession> ImportSessions => Set<ImportSession>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Rule> Rules => Set<Rule>();

    public DbSet<CategoryCache> CategoryCache => Set<CategoryCache>();

    public DbSet<AiUsageEntry> AiUsageEntries => Set<AiUsageEntry>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<Alert> Alerts => Set<Alert>();

    public DbSet<Goal> Goals => Set<Goal>();

    public DbSet<GoalContribution> GoalContributions => Set<GoalContribution>();

    public DbSet<GoalSnapshot> GoalSnapshots => Set<GoalSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SchemaInfoEntry>(entity =>
        {
            entity.ToTable("_SchemaInfo");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Version).IsUnique();
            entity.Property(x => x.Version).IsRequired();
            entity.Property(x => x.AppVersion).IsRequired();
        });

        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ImportSessionConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionConfiguration());
        modelBuilder.ApplyConfiguration(new RuleConfiguration());
        modelBuilder.ApplyConfiguration(new CategoryCacheConfiguration());
        modelBuilder.ApplyConfiguration(new AiUsageEntryConfiguration());
        modelBuilder.ApplyConfiguration(new AppSettingConfiguration());
        modelBuilder.ApplyConfiguration(new AlertConfiguration());
        modelBuilder.ApplyConfiguration(new GoalConfiguration());
        modelBuilder.ApplyConfiguration(new GoalContributionConfiguration());
        modelBuilder.ApplyConfiguration(new GoalSnapshotConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Hard rule #1: every monetary column is decimal(18,2).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }
}
