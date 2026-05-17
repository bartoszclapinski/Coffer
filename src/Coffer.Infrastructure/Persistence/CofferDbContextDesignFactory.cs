using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Coffer.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef migrations add</c> to construct a <see cref="CofferDbContext"/>
/// without a real DEK. Generating migrations does not require an actual connection.
/// </summary>
public sealed class CofferDbContextDesignFactory : IDesignTimeDbContextFactory<CofferDbContext>
{
    public CofferDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CofferDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        return new CofferDbContext(options);
    }
}
