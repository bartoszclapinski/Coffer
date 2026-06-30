using System.Security.Cryptography;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Tests.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Planning;

/// <summary>
/// Spins up a migrated SQLCipher database in a temp directory and exposes a context factory plus
/// seed helpers, mirroring <see cref="TransactionsSchemaTests"/>'s setup for the planning read-side.
/// </summary>
public abstract class PlanningDbTestBase : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly byte[] _dek;

    protected SqliteTestDbContextFactory Factory { get; }

    protected PlanningDbTestBase()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Coffer.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "coffer.db");
        _dek = RandomNumberGenerator.GetBytes(32);

        using (var db = new CofferDbContext(BuildOptions()))
        {
            db.Database.Migrate();
        }

        Factory = new SqliteTestDbContextFactory(_dbPath, _dek);
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

        GC.SuppressFinalize(this);
    }

    private protected async Task SeedTransactionsAsync(IEnumerable<Transaction> transactions)
    {
        await using var db = Factory.CreateDbContext();
        db.Transactions.AddRange(transactions);
        await db.SaveChangesAsync();
    }

    private protected static Account NewAccount() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test account",
        BankCode = "PKO_BP",
        AccountNumber = "PL00000000000000000000000000",
        Currency = "PLN",
        Type = AccountType.Checking,
        CreatedAt = DateTime.UtcNow,
    };

    private protected static ImportSession NewImportSession(DateOnly periodFrom, DateOnly periodTo) => new()
    {
        Id = Guid.NewGuid(),
        FileName = "statement.csv",
        FileHash = "FILEHASH-" + Guid.NewGuid().ToString("N"),
        BankCode = "PKO_BP",
        PeriodFrom = periodFrom,
        PeriodTo = periodTo,
        ImportedAt = DateTime.UtcNow,
        Status = ImportStatus.Completed,
    };

    private protected static Transaction NewTransaction(
        Account account,
        ImportSession session,
        DateOnly date,
        decimal amount,
        string? merchant = null,
        Guid? categoryId = null) => new()
        {
            Id = Guid.NewGuid(),
            Account = account,
            AccountId = account.Id,
            ImportSession = session,
            ImportSessionId = session.Id,
            Date = date,
            Amount = amount,
            Currency = "PLN",
            Description = merchant ?? "Test transaction",
            NormalizedDescription = (merchant ?? "test transaction").ToLowerInvariant(),
            Merchant = merchant,
            CategoryId = categoryId,
            Hash = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
        };

    private DbContextOptions<CofferDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<CofferDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=False;")
            .AddInterceptors(new Coffer.Infrastructure.Persistence.Encryption.SqlCipherKeyInterceptor(_dek))
            .Options;
}
