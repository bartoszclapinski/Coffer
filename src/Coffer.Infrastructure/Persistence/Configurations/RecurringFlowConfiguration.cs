using Coffer.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coffer.Infrastructure.Persistence.Configurations;

public sealed class RecurringFlowConfiguration : IEntityTypeConfiguration<RecurringFlow>
{
    public void Configure(EntityTypeBuilder<RecurringFlow> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RecurringFlows");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired();

        // Enums persist as readable strings (parity with Alert/AiUsageEntry).
        builder.Property(e => e.Direction).HasConversion<string>().IsRequired();
        builder.Property(e => e.Source).HasConversion<string>().IsRequired();

        builder.Property(e => e.Currency).IsRequired();

        // The projection only ever loads active flows.
        builder.HasIndex(e => e.IsActive);
    }
}
