using Coffer.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coffer.Infrastructure.Persistence.Configurations;

public sealed class AdvisorReportConfiguration : IEntityTypeConfiguration<AdvisorReport>
{
    public void Configure(EntityTypeBuilder<AdvisorReport> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AdvisorReports");
        builder.HasKey(e => e.Id);

        // The daily job writes one report per day; the UI loads the latest by date.
        builder.HasIndex(e => e.Date).IsUnique();

        // A report owns its lines: deleting the report takes its risks and suggestions with it.
        builder.HasMany(e => e.Entries)
            .WithOne()
            .HasForeignKey(s => s.ReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AdvisorSuggestionConfiguration : IEntityTypeConfiguration<AdvisorSuggestion>
{
    public void Configure(EntityTypeBuilder<AdvisorSuggestion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AdvisorSuggestions");
        builder.HasKey(e => e.Id);

        // Enum persists as a readable string (parity with the rest of the schema).
        builder.Property(e => e.Kind).HasConversion<string>().IsRequired();

        builder.Property(e => e.Title).IsRequired();
        builder.Property(e => e.Description).IsRequired();
    }
}
