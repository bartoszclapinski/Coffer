using Coffer.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coffer.Infrastructure.Persistence.Configurations;

public sealed class GoalContributionConfiguration : IEntityTypeConfiguration<GoalContribution>
{
    public void Configure(EntityTypeBuilder<GoalContribution> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GoalContributions");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Source).HasConversion<string>().IsRequired();

        builder.HasIndex(e => e.GoalId);
    }
}
