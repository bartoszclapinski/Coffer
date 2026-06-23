using System.Security.Cryptography;
using Coffer.Core.Anomalies;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Anomalies;
using Coffer.Infrastructure.Anomalies.Detectors;
using Coffer.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Anomalies;

public class AnomalyDetectionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteTestDbContextFactory _factory;

    private Guid _accountId;
    private Guid _sessionId;

    public AnomalyDetectionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Coffer.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "coffer.db");
        var dek = RandomNumberGenerator.GetBytes(32);
        _factory = new SqliteTestDbContextFactory(_dbPath, dek);
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
    public async Task Run_EmptyVault_RaisesNothing()
    {
        await using (var db = _factory.CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        var added = await NewService().RunAsync(CancellationToken.None);

        added.Should().Be(0);
    }

    [Fact]
    public async Task Run_PersistsNewMerchantAlert()
    {
        await SeedNewMerchantScenarioAsync();

        var added = await NewService().RunAsync(CancellationToken.None);

        added.Should().Be(1);

        await using var db = _factory.CreateDbContext();
        var alert = await db.Alerts.SingleAsync();
        alert.Type.Should().Be(AnomalyType.NewMerchant);
        alert.Status.Should().Be(AlertStatus.New);
        alert.Signature.Should().StartWith("new-merchant:");
    }

    [Fact]
    public async Task Run_IsIdempotent_DoesNotDuplicateOnRescan()
    {
        await SeedNewMerchantScenarioAsync();

        (await NewService().RunAsync(CancellationToken.None)).Should().Be(1);
        (await NewService().RunAsync(CancellationToken.None)).Should().Be(0);

        await using var db = _factory.CreateDbContext();
        (await db.Alerts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Run_NeverResurrectsDismissedAlert()
    {
        await SeedNewMerchantScenarioAsync();
        await NewService().RunAsync(CancellationToken.None);

        await using (var db = _factory.CreateDbContext())
        {
            var alert = await db.Alerts.SingleAsync();
            alert.Status = AlertStatus.Dismissed;
            alert.ResolvedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var added = await NewService().RunAsync(CancellationToken.None);

        added.Should().Be(0, "a dismissed signature is never re-raised");

        await using var verify = _factory.CreateDbContext();
        var only = await verify.Alerts.SingleAsync();
        only.Status.Should().Be(AlertStatus.Dismissed);
    }

    private AnomalyDetectionService NewService()
    {
        IAnomalyDetector[] detectors =
        [
            new HighAmountInCategoryDetector(),
            new NewMerchantDetector(),
            new CategorySpikeDetector(),
            new DuplicatePaymentDetector(),
            new MissingRecurrenceDetector(),
        ];
        return new AnomalyDetectionService(_factory, detectors, NullLogger<AnomalyDetectionService>.Instance);
    }

    private async Task SeedNewMerchantScenarioAsync()
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();

        _accountId = Guid.NewGuid();
        _sessionId = Guid.NewGuid();

        db.Accounts.Add(new Account
        {
            Id = _accountId,
            Name = "PKO",
            BankCode = "PKO_BP",
            AccountNumber = "PL01",
            Currency = "PLN",
            Type = AccountType.Checking,
            CreatedAt = DateTime.UtcNow,
        });
        db.ImportSessions.Add(new ImportSession
        {
            Id = _sessionId,
            FileName = "seed.csv",
            FileHash = "SEEDHASH",
            BankCode = "PKO_BP",
            PeriodFrom = new DateOnly(2025, 1, 1),
            PeriodTo = new DateOnly(2026, 12, 31),
            ImportedAt = DateTime.UtcNow,
            Status = ImportStatus.Completed,
        });

        // Baseline merchant well before the recent window; a brand-new merchant inside it.
        db.Transactions.Add(NewTx(new DateOnly(2026, 3, 1), -50m, "Stary"));
        db.Transactions.Add(NewTx(new DateOnly(2026, 6, 15), -80m, "Nowy"));
        await db.SaveChangesAsync();
    }

    private Transaction NewTx(DateOnly date, decimal amount, string merchant) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = _accountId,
            ImportSessionId = _sessionId,
            Date = date,
            Amount = amount,
            Currency = "PLN",
            Description = merchant,
            NormalizedDescription = merchant.ToUpperInvariant(),
            Merchant = merchant,
            Hash = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
        };
}
