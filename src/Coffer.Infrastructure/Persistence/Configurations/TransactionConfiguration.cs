using Coffer.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coffer.Infrastructure.Persistence.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Currency).IsRequired();
        builder.Property(t => t.Description).IsRequired();
        builder.Property(t => t.NormalizedDescription).IsRequired();
        builder.Property(t => t.Hash).IsRequired();

        // Indexes per docs/architecture/02-database-and-encryption.md.
        builder.HasIndex(t => t.Date);
        builder.HasIndex(t => t.AccountId);
        builder.HasIndex(t => new { t.Date, t.AccountId });
        builder.HasIndex(t => t.Hash).IsUnique();
        builder.HasIndex(t => t.NormalizedDescription);
        builder.HasIndex(t => t.CategoryId);

        // Financial rows are never cascade-deleted with their parent: deleting an
        // account or import session must be a deliberate, blocked-by-FK action.
        builder.HasOne(t => t.Account)
            .WithMany()
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.ImportSession)
            .WithMany()
            .HasForeignKey(t => t.ImportSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Category)
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
