using Coffer.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coffer.Infrastructure.Persistence.Configurations;

public sealed class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Rules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Pattern).IsRequired();

        // Evaluated in priority order on every uncached transaction — index it.
        builder.HasIndex(r => r.Priority);

        builder.HasOne(r => r.Category)
            .WithMany()
            .HasForeignKey(r => r.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
