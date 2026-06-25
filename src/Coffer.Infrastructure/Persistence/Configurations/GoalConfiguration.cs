using Coffer.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coffer.Infrastructure.Persistence.Configurations;

public sealed class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Goals");
        builder.HasKey(e => e.Id);

        // Enums persist as readable strings (parity with the rest of the schema).
        builder.Property(e => e.Type).HasConversion<string>().IsRequired();
        builder.Property(e => e.Priority).HasConversion<string>().IsRequired();

        builder.Property(e => e.Name).IsRequired();
        builder.Property(e => e.Currency).IsRequired();

        // The advisor lists/evaluates active goals; archived ones are filtered out.
        builder.HasIndex(e => e.IsArchived);

        // A goal's contributions and snapshots are owned by it: archiving keeps them, but a
        // hard delete takes them along (no orphan projection history).
        builder.HasMany(e => e.Contributions)
            .WithOne()
            .HasForeignKey(c => c.GoalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Snapshots)
            .WithOne()
            .HasForeignKey(s => s.GoalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
