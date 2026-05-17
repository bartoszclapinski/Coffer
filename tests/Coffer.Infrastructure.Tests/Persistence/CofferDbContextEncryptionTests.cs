using System.Security.Cryptography;
using System.Text;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Persistence.Encryption;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Persistence;

public class CofferDbContextEncryptionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public CofferDbContextEncryptionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Coffer.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "coffer.db");
    }

    public void Dispose()
    {
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

    [Fact]
    public async Task WriteThenRead_WithSameDek_RoundTripsEntry()
    {
        var dek = RandomNumberGenerator.GetBytes(32);

        await using (var db = CreateContext(dek))
        {
            await db.Database.MigrateAsync();
            db.SchemaInfo.Add(new SchemaInfoEntry
            {
                Version = "RoundtripTest",
                MigratedAt = DateTime.UtcNow,
                AppVersion = "1.0.0",
            });
            await db.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();

        await using (var db = CreateContext(dek))
        {
            var entries = await db.SchemaInfo.ToListAsync();
            entries.Should().HaveCount(1);
            entries[0].Version.Should().Be("RoundtripTest");
        }
    }

    [Fact]
    public async Task Read_WithDifferentDek_Throws()
    {
        var dek1 = RandomNumberGenerator.GetBytes(32);
        var dek2 = RandomNumberGenerator.GetBytes(32);

        await using (var db = CreateContext(dek1))
        {
            await db.Database.MigrateAsync();
            db.SchemaInfo.Add(new SchemaInfoEntry
            {
                Version = "OriginalEntry",
                MigratedAt = DateTime.UtcNow,
                AppVersion = "1.0.0",
            });
            await db.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();

        await using var db2 = CreateContext(dek2);
        var act = async () => await db2.SchemaInfo.ToListAsync();

        // SQLITE_NOTADB = 26 is the canonical SQLCipher response when the wrong key is
        // supplied — locking up the assertion to that code prevents the test from
        // passing on generic SqliteException categories (disk full, schema mismatch, …).
        var thrown = await act.Should().ThrowAsync<SqliteException>();
        thrown.Which.SqliteErrorCode.Should().Be(26);
    }

    [Fact]
    public async Task RawFileBytes_DoNotContainPlaintextVersionString()
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        const string sentinel = "TEST_SENTINEL_12345";

        await using (var db = CreateContext(dek))
        {
            await db.Database.MigrateAsync();
            db.SchemaInfo.Add(new SchemaInfoEntry
            {
                Version = sentinel,
                MigratedAt = DateTime.UtcNow,
                AppVersion = "1.0.0",
            });
            await db.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();

        var fileBytes = await File.ReadAllBytesAsync(_dbPath);
        var sentinelBytes = Encoding.UTF8.GetBytes(sentinel);

        ContainsSequence(fileBytes, sentinelBytes).Should().BeFalse(
            "the sentinel string should not appear in the encrypted database file");
    }

    private CofferDbContext CreateContext(byte[] dek)
    {
        // Pooling=False forces the SqliteConnection to open a fresh underlying connection
        // every time, which guarantees SqlCipherKeyInterceptor.ConnectionOpened fires and
        // PRAGMA key runs. Pooled connections in EF Core 9 + Microsoft.Data.Sqlite skip
        // the open event, leaving the second context unable to read the encrypted file.
        var options = new DbContextOptionsBuilder<CofferDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=False;")
            .AddInterceptors(new SqlCipherKeyInterceptor(dek))
            .Options;
        return new CofferDbContext(options);
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
        {
            return false;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }
        return false;
    }
}
