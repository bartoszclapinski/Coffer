using Coffer.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coffer.Infrastructure.Persistence.Configurations;

public sealed class AiUsageEntryConfiguration : IEntityTypeConfiguration<AiUsageEntry>
{
    public void Configure(EntityTypeBuilder<AiUsageEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AiUsageEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provider).IsRequired();
        builder.Property(e => e.Model).IsRequired();
        builder.Property(e => e.Purpose).IsRequired();

        // Month-to-date queries filter and group by these.
        builder.HasIndex(e => e.At);
        builder.HasIndex(e => e.Purpose);

        // Cost columns override the global decimal(18,2) convention: a single
        // categorisation call can cost a tiny fraction of a grosz, which (18,2)
        // would round to 0.00 and make the ledger under-report.
        builder.Property(e => e.EstimatedCostUsd).HasPrecision(18, 6);
        builder.Property(e => e.EstimatedCostPln).HasPrecision(18, 6);
    }
}
