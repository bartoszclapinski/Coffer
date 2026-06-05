using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Persistence.Encryption;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Persistence;

/// <summary>
/// An <see cref="IDbContextFactory{TContext}"/> over a single SQLCipher database
/// file, for use cases / queries that take a factory. Uses the same
/// <c>Pooling=False</c> pattern as the schema tests so each created context opens
/// its own connection and temp files can be deleted after the test.
/// </summary>
public sealed class SqliteTestDbContextFactory : IDbContextFactory<CofferDbContext>
{
    private readonly DbContextOptions<CofferDbContext> _options;

    public SqliteTestDbContextFactory(string dbPath, byte[] dek)
    {
        _options = new DbContextOptionsBuilder<CofferDbContext>()
            .UseSqlite($"Data Source={dbPath};Pooling=False;")
            .AddInterceptors(new SqlCipherKeyInterceptor(dek))
            .Options;
    }

    public CofferDbContext CreateDbContext() => new(_options);
}
