using Coffer.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coffer.Infrastructure.Persistence.Configurations;

public sealed class CategoryCacheConfiguration : IEntityTypeConfiguration<CategoryCache>
{
    public void Configure(EntityTypeBuilder<CategoryCache> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CategoryCache");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.NormalizedDescription).IsRequired();

        // Exact-key lookup on every categorisation; the key is the cache identity.
        builder.HasIndex(c => c.NormalizedDescription).IsUnique();

        builder.HasOne(c => c.Category)
            .WithMany()
            .HasForeignKey(c => c.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
