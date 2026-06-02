using Coffer.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coffer.Infrastructure.Persistence.Configurations;

public sealed class ImportSessionConfiguration : IEntityTypeConfiguration<ImportSession>
{
    public void Configure(EntityTypeBuilder<ImportSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ImportSessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.FileName).IsRequired();
        builder.Property(s => s.FileHash).IsRequired();
        builder.Property(s => s.BankCode).IsRequired();

        builder.HasIndex(s => s.FileHash);
    }
}
