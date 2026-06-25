using Coffer.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coffer.Infrastructure.Persistence.Configurations;

public sealed class GoalSnapshotConfiguration : IEntityTypeConfiguration<GoalSnapshot>
{
    public void Configure(EntityTypeBuilder<GoalSnapshot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GoalSnapshots");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Status).HasConversion<string>().IsRequired();

        // The daily job writes one row per goal per day; the UI reads a goal's history by date.
        builder.HasIndex(e => new { e.GoalId, e.Date });
    }
}
