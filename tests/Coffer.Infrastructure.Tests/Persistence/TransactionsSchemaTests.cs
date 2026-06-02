using System.Security.Cryptography;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Persistence.Encryption;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Persistence;

public class TransactionsSchemaTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly byte[] _dek;

    public TransactionsSchemaTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Coffer.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "coffer.db");
        _dek = RandomNumberGenerator.GetBytes(32);
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
    public async Task Migrate_CreatesAllSchemaTables()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")
            .ToListAsync();

        tables.Should().Contain(new[] { "Accounts", "Categories", "ImportSessions", "Transactions" });
    }

    [Fact]
    public async Task Migrate_CreatesExpectedTransactionIndexes()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var indexes = await db.Database
            .SqlQueryRaw<string>(
                "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='Transactions'")
            .ToListAsync();

        indexes.Should().Contain(new[]
        {
            "IX_Transactions_Date",
            "IX_Transactions_AccountId",
            "IX_Transactions_Date_AccountId",
            "IX_Transactions_Hash",
            "IX_Transactions_NormalizedDescription",
            "IX_Transactions_CategoryId",
        });
    }

    [Fact]
    public async Task HashUniqueIndex_RejectsDuplicateHash()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var account = SeedAccount(db);
        var session = SeedImportSession(db);
        await db.SaveChangesAsync();

        db.Transactions.Add(NewTransaction(account.Id, session.Id, hash: "DUPLICATE_HASH"));
        await db.SaveChangesAsync();

        db.Transactions.Add(NewTransaction(account.Id, session.Id, hash: "DUPLICATE_HASH"));
        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DecimalAmount_RoundTripsAtFullPrecision()
    {
        var amount = 1234567890123456.78m; // 18 significant digits, 2 fractional
        Guid id;

        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
            var account = SeedAccount(db);
            var session = SeedImportSession(db);
            var transaction = NewTransaction(account.Id, session.Id, hash: "AMOUNT_HASH");
            transaction.Amount = amount;
            id = transaction.Id;
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();

        await using (var db = CreateContext())
        {
            var stored = await db.Transactions.SingleAsync(t => t.Id == id);
            stored.Amount.Should().Be(amount);
        }
    }

    [Fact]
    public async Task DateOnly_RoundTripsThroughSqlCipher()
    {
        var date = new DateOnly(2026, 5, 31);
        var bookingDate = new DateOnly(2026, 6, 1);
        Guid id;

        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
            var account = SeedAccount(db);
            var session = SeedImportSession(db);
            var transaction = NewTransaction(account.Id, session.Id, hash: "DATE_HASH");
            transaction.Date = date;
            transaction.BookingDate = bookingDate;
            id = transaction.Id;
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();

        await using (var db = CreateContext())
        {
            var stored = await db.Transactions.SingleAsync(t => t.Id == id);
            stored.Date.Should().Be(date);
            stored.BookingDate.Should().Be(bookingDate);
        }
    }

    private static Account SeedAccount(CofferDbContext db)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Test account",
            BankCode = "PKO_BP",
            AccountNumber = "PL00000000000000000000000000",
            Currency = "PLN",
            Type = AccountType.Checking,
            CreatedAt = DateTime.UtcNow,
        };
        db.Accounts.Add(account);
        return account;
    }

    private static ImportSession SeedImportSession(CofferDbContext db)
    {
        var session = new ImportSession
        {
            Id = Guid.NewGuid(),
            FileName = "statement.csv",
            FileHash = "FILEHASH",
            BankCode = "PKO_BP",
            PeriodFrom = new DateOnly(2026, 5, 1),
            PeriodTo = new DateOnly(2026, 5, 31),
            ImportedAt = DateTime.UtcNow,
            Status = ImportStatus.Completed,
        };
        db.ImportSessions.Add(session);
        return session;
    }

    private static Transaction NewTransaction(Guid accountId, Guid sessionId, string hash) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = accountId,
        ImportSessionId = sessionId,
        Date = new DateOnly(2026, 5, 15),
        Amount = -42.50m,
        Currency = "PLN",
        Description = "Test transaction",
        NormalizedDescription = "test transaction",
        Hash = hash,
        CreatedAt = DateTime.UtcNow,
    };

    private CofferDbContext CreateContext()
    {
        // Pooling=False — see CofferDbContextEncryptionTests for rationale.
        var options = new DbContextOptionsBuilder<CofferDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=False;")
            .AddInterceptors(new SqlCipherKeyInterceptor(_dek))
            .Options;
        return new CofferDbContext(options);
    }
}
