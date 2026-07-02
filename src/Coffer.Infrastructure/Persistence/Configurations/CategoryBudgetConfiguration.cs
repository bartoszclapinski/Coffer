using Coffer.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coffer.Infrastructure.Persistence.Configurations;

public sealed class CategoryBudgetConfiguration : IEntityTypeConfiguration<CategoryBudget>
{
    public void Configure(EntityTypeBuilder<CategoryBudget> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CategoryBudgets");
        builder.HasKey(e => e.Id);

        // Money is decimal(18,2) by the global convention (hard rule #1); Currency is non-null (rule #9).
        builder.Property(e => e.Currency).IsRequired();

        // A budget always belongs to a real category; if the category is removed, its budget goes with it.
        builder.HasOne(e => e.Category)
            .WithMany()
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // The tracking query loads active budgets by category.
        builder.HasIndex(e => e.CategoryId);
        builder.HasIndex(e => e.IsActive);
    }
}
