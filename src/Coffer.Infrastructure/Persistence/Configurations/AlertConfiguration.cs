using Coffer.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coffer.Infrastructure.Persistence.Configurations;

public sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Alerts");
        builder.HasKey(e => e.Id);

        // Enums persist as readable strings (parity with AiUsageEntry.Purpose).
        builder.Property(e => e.Type).HasConversion<string>().IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().IsRequired();

        builder.Property(e => e.Signature).IsRequired();
        builder.Property(e => e.Title).IsRequired();
        builder.Property(e => e.Description).IsRequired();

        // Dedup key: a rescan upserts by signature and never re-raises a dismissed anomaly.
        builder.HasIndex(e => e.Signature).IsUnique();

        // The active-list query filters by status, newest first.
        builder.HasIndex(e => new { e.Status, e.DetectedAt });
    }
}
