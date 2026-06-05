using System.Security.Cryptography;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Categorization;

/// <summary>
/// Shared SQLCipher harness for the categorisation tests: a single encrypted database
/// file with <c>Pooling=False</c>, migrated on first use, cleaned up after the test.
/// </summary>
public abstract class CategorizationDbTest : IDisposable
{
    private readonly string _tempDir;

    protected CategorizationDbTest()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Coffer.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var dbPath = Path.Combine(_tempDir, "coffer.db");
        var dek = RandomNumberGenerator.GetBytes(32);
        Factory = new SqliteTestDbContextFactory(dbPath, dek);
    }

    protected SqliteTestDbContextFactory Factory { get; }

    protected async Task<CofferDbContext> MigratedContextAsync()
    {
        var db = Factory.CreateDbContext();
        await db.Database.MigrateAsync();
        return db;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
