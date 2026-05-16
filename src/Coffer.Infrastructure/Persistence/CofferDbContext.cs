using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Persistence;

public sealed class CofferDbContext : DbContext
{
    public CofferDbContext(DbContextOptions<CofferDbContext> options)
        : base(options)
    {
    }

    public DbSet<SchemaInfoEntry> SchemaInfo => Set<SchemaInfoEntry>();

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
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Hard rule #1: every monetary column is decimal(18,2).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }
}
