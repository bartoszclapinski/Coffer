using System.Text;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Persistence.Encryption;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Backup;

/// <summary>
/// Builds real SQLCipher databases for backup tests: migrated with the app's schema, keyed through the same
/// <see cref="SqlCipherKeyInterceptor"/> the app uses, filled with synthetic <c>SchemaInfo</c> rows.
/// </summary>
internal static class SqlCipherTestDatabase
{
    private static readonly byte[] _plaintextHeader = Encoding.ASCII.GetBytes("SQLite format 3");

    /// <summary>A context on <paramref name="path"/> keyed with <paramref name="dek"/>.</summary>
    public static CofferDbContext CreateContext(string path, byte[] dek, bool pooling)
    {
        var options = new DbContextOptionsBuilder<CofferDbContext>()
            .UseSqlite($"Data Source={path};Pooling={pooling};")
            .AddInterceptors(new SqlCipherKeyInterceptor((byte[])dek.Clone()))
            .Options;
        return new CofferDbContext(options);
    }

    /// <summary>Creates and migrates the database, then adds <paramref name="rows"/> synthetic rows.</summary>
    public static async Task CreateAsync(string path, byte[] dek, int rows)
    {
        await using var db = CreateContext(path, dek, pooling: false);
        await db.Database.MigrateAsync();
        await AddRowsAsync(db, rows, "seed");
    }

    /// <summary>Adds <paramref name="rows"/> synthetic rows through <paramref name="db"/>.</summary>
    public static async Task AddRowsAsync(CofferDbContext db, int rows, string tag)
    {
        for (var i = 0; i < rows; i++)
        {
            db.SchemaInfo.Add(new SchemaInfoEntry
            {
                Version = $"{tag}-{i}",
                MigratedAt = DateTime.UtcNow,
                AppVersion = "0.0.0-test",
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>The row count read through a fresh, non-pooled connection keyed with <paramref name="dek"/>.</summary>
    public static async Task<int> CountRowsAsync(string path, byte[] dek)
    {
        await using var db = CreateContext(path, dek, pooling: false);
        return await db.SchemaInfo.CountAsync();
    }

    /// <summary>Whether the file starts with SQLite's plaintext header (i.e. it is NOT encrypted).</summary>
    public static bool HasPlaintextHeader(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes.Length >= _plaintextHeader.Length
            && bytes.AsSpan(0, _plaintextHeader.Length).SequenceEqual(_plaintextHeader);
    }
}
