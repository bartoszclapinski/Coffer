using Coffer.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coffer.Infrastructure.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Accounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).IsRequired();
        builder.Property(a => a.BankCode).IsRequired();
        builder.Property(a => a.AccountNumber).IsRequired();
        builder.Property(a => a.Currency).IsRequired();

        builder.HasIndex(a => a.IsArchived);
    }
}
