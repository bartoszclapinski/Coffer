using System.Security.Cryptography;
using Coffer.Core.Domain;
using Coffer.Core.Import;
using Coffer.Core.Parsing;
using Coffer.Infrastructure.Categorization;
using Coffer.Infrastructure.Import;
using Coffer.Infrastructure.Parsing;
using Coffer.Infrastructure.Parsing.Ai;
using Coffer.Infrastructure.Parsing.Pko;
using Coffer.Infrastructure.Parsing.Polish;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Tests.Parsing.Pko;
using Coffer.Infrastructure.Tests.Persistence;
using Coffer.Shared.Parsing;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Import;

public class ImportStatementUseCaseTests : IDisposable
{
    private const string AccountNumber = "PL60102010260000042270201111";

    private const string PkoHeader =
        "\"Data operacji\",\"Data waluty\",\"Typ transakcji\",\"Kwota\",\"Waluta\",\"Saldo po transakcji\",\"Opis transakcji\",\"\",\"\"\n";

    // Two genuinely identical card payments on the same day (same amount, same description),
    // plus one unrelated row. Synthetic merchant names only.
    private const string CoffeeRow =
        "\"2026-02-03\",\"2026-02-03\",\"Platnosc karta\",\"-12.00\",\"PLN\",\"500.00\",\"Platnosc karta\",\"Nazwa nadawcy/odbiorcy: KAWIARNIA TESTOWA\",\"Tytul: Kawa\"\n";

    private const string BakeryRow =
        "\"2026-02-03\",\"2026-02-03\",\"Platnosc karta\",\"-7.50\",\"PLN\",\"476.00\",\"Platnosc karta\",\"Nazwa nadawcy/odbiorcy: PIEKARNIA TESTOWA\",\"Tytul: Chleb\"\n";

    private const string IdenticalRowsCsv = PkoHeader + CoffeeRow + CoffeeRow + BakeryRow;

    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly byte[] _dek;
    private readonly SqliteTestDbContextFactory _factory;

    public ImportStatementUseCaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Coffer.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "coffer.db");
        _dek = RandomNumberGenerator.GetBytes(32);
        _factory = new SqliteTestDbContextFactory(_dbPath, _dek);
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
    public async Task Execute_ImportsGoldenCsv_UnderOneSession()
    {
        var accountId = await SeedAccountAsync();
        var useCase = CreateUseCase();

        var input = CsvStatementInputFactory.FromGoldenFile();
        var summary = await useCase.ExecuteAsync(new ImportRequest(input, accountId), null, CancellationToken.None);

        summary.Added.Should().Be(8);
        summary.Skipped.Should().Be(0);
        summary.AlreadyImported.Should().BeFalse();

        await using var db = _factory.CreateDbContext();
        (await db.Transactions.CountAsync()).Should().Be(8);
        (await db.ImportSessions.CountAsync()).Should().Be(1);
        var session = await db.ImportSessions.SingleAsync();
        session.TransactionsAdded.Should().Be(8);
        (await db.Transactions.AllAsync(t => t.ImportSessionId == session.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_ReimportingSameFile_AddsNothing()
    {
        var accountId = await SeedAccountAsync();
        var useCase = CreateUseCase();

        var first = CsvStatementInputFactory.FromGoldenFile();
        await useCase.ExecuteAsync(new ImportRequest(first, accountId), null, CancellationToken.None);

        var second = CsvStatementInputFactory.FromGoldenFile();
        var summary = await useCase.ExecuteAsync(new ImportRequest(second, accountId), null, CancellationToken.None);

        summary.Added.Should().Be(0);
        summary.Skipped.Should().Be(8);
        summary.AlreadyImported.Should().BeTrue("the same file hash was recorded by the first run");

        await using var db = _factory.CreateDbContext();
        (await db.Transactions.CountAsync()).Should().Be(8, "dedup by Hash prevents duplicate rows");
        (await db.ImportSessions.CountAsync()).Should().Be(2, "each run still records its own session");
    }

    [Fact]
    public async Task Execute_IdenticalRowsInOneFile_ImportsEachAsSeparateTransaction()
    {
        var accountId = await SeedAccountAsync();
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(
            new ImportRequest(CsvStatementInputFactory.FromCsv(IdenticalRowsCsv), accountId), null, CancellationToken.None);

        summary.Added.Should().Be(3, "the second coffee is a real transaction, not a duplicate");
        summary.Skipped.Should().Be(0);
        summary.WithinFileRepeats.Should().Be(1);

        await using var db = _factory.CreateDbContext();
        var coffees = await db.Transactions.Where(t => t.Amount == -12.00m).ToListAsync();
        coffees.Should().HaveCount(2);

        // The first occurrence keeps the pre-Sprint-30 hash; the second is occurrence-indexed.
        var normalized = coffees[0].NormalizedDescription;
        var date = new DateOnly(2026, 2, 3);
        coffees.Select(t => t.Hash).Should().BeEquivalentTo(
        [
            TransactionHash.Compute(AccountNumber, date, -12.00m, normalized),
            TransactionHash.Compute(AccountNumber, date, -12.00m, normalized, occurrence: 2),
        ]);
    }

    [Fact]
    public async Task Execute_ExistingHashesOfGoldenFile_AreUnchanged()
    {
        // The golden file has no repeated rows, so every stored hash must equal the legacy
        // four-part hash — exactly what versions before occurrence indexing stored.
        var accountId = await SeedAccountAsync();
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(
            new ImportRequest(CsvStatementInputFactory.FromGoldenFile(), accountId), null, CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        var stored = await db.Transactions.ToListAsync();
        stored.Should().HaveCount(8).And.OnlyContain(t =>
            t.Hash == TransactionHash.Compute(AccountNumber, t.Date, t.Amount, DescriptionNormalizer.Normalize(t.Description)));
    }

    [Fact]
    public async Task Execute_ReimportingFileWithIdenticalRows_AddsNothing()
    {
        var accountId = await SeedAccountAsync();
        var useCase = CreateUseCase();
        await useCase.ExecuteAsync(
            new ImportRequest(CsvStatementInputFactory.FromCsv(IdenticalRowsCsv), accountId), null, CancellationToken.None);

        var summary = await useCase.ExecuteAsync(
            new ImportRequest(CsvStatementInputFactory.FromCsv(IdenticalRowsCsv), accountId), null, CancellationToken.None);

        summary.Added.Should().Be(0);
        summary.Skipped.Should().Be(3, "the indexed repeat is also deduplicated across imports");
        summary.WithinFileRepeats.Should().Be(0, "repeats are reported only for rows actually added");

        await using var db = _factory.CreateDbContext();
        (await db.Transactions.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Execute_OverlappingExportWithMoreRepeats_AddsOnlyTheNewOccurrence()
    {
        // A mid-day export held one coffee; the full-day export later holds both plus the bakery.
        var accountId = await SeedAccountAsync();
        var useCase = CreateUseCase();
        await useCase.ExecuteAsync(
            new ImportRequest(CsvStatementInputFactory.FromCsv(PkoHeader + CoffeeRow), accountId), null, CancellationToken.None);

        var summary = await useCase.ExecuteAsync(
            new ImportRequest(CsvStatementInputFactory.FromCsv(IdenticalRowsCsv), accountId), null, CancellationToken.None);

        summary.Added.Should().Be(2, "only the second coffee and the bakery are new");
        summary.Skipped.Should().Be(1);

        await using var db = _factory.CreateDbContext();
        (await db.Transactions.CountAsync(t => t.Amount == -12.00m)).Should().Be(2);
    }

    [Fact]
    public async Task Execute_RowImportedBeforeOccurrenceIndexing_StillDeduplicates()
    {
        // Simulates a vault where an older version imported this file and collapsed the two
        // coffees into one row stored under the legacy hash.
        var accountId = await SeedAccountAsync();
        const string coffeeDescription = "Platnosc karta Nazwa nadawcy/odbiorcy: KAWIARNIA TESTOWA Tytul: Kawa";
        var normalized = DescriptionNormalizer.Normalize(coffeeDescription);
        var date = new DateOnly(2026, 2, 3);
        await using (var db = _factory.CreateDbContext())
        {
            var session = new ImportSession
            {
                Id = Guid.NewGuid(),
                FileName = "older.csv",
                FileHash = "OLDER",
                BankCode = "PKO_BP",
                PeriodFrom = date,
                PeriodTo = date,
                ImportedAt = DateTime.UtcNow,
                TransactionsAdded = 1,
                Status = ImportStatus.Completed,
            };
            db.ImportSessions.Add(session);
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Date = date,
                Amount = -12.00m,
                Currency = "PLN",
                Description = coffeeDescription,
                NormalizedDescription = normalized,
                Hash = TransactionHash.Compute(AccountNumber, date, -12.00m, normalized),
                ImportSessionId = session.Id,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var summary = await CreateUseCase().ExecuteAsync(
            new ImportRequest(CsvStatementInputFactory.FromCsv(PkoHeader + CoffeeRow + CoffeeRow), accountId),
            null,
            CancellationToken.None);

        summary.Added.Should().Be(1, "only the previously collapsed second coffee is new");
        summary.Skipped.Should().Be(1, "the first coffee matches the legacy hash already stored");
        summary.WithinFileRepeats.Should().Be(1);
    }

    [Fact]
    public async Task Execute_UnexpectedPkoLayout_ThrowsLayoutExceptionAndSavesNothing()
    {
        var accountId = await SeedAccountAsync();
        var useCase = CreateUseCase();
        // Same labels (so the detector picks PKO) but ';' separators, as after a spreadsheet re-save.
        const string resaved =
            "Data operacji;Data waluty;Typ transakcji;Kwota;Waluta;Saldo po transakcji;Opis transakcji\n" +
            "2026-02-03;2026-02-03;Platnosc karta;-12,00;PLN;500,00;Platnosc karta\n";

        var act = async () => await useCase.ExecuteAsync(
            new ImportRequest(CsvStatementInputFactory.FromCsv(resaved), accountId), null, CancellationToken.None);

        await act.Should().ThrowAsync<UnsupportedStatementLayoutException>();
        await using var db = _factory.CreateDbContext();
        (await db.ImportSessions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Execute_DoesNotLogStatementFileName()
    {
        var accountId = await SeedAccountAsync();
        var logger = new RecordingLogger();
        var useCase = new ImportStatementUseCase(
            _factory,
            new FingerprintBankDetector(),
            new StatementParserRegistry([new PkoHistoriaCsvParser()]),
            new RuleCacheCategorizer(_factory, new RuleEngine(NullLogger<RuleEngine>.Instance)),
            logger);
        const string fileName = "Historia_rachunku_JAN_TESTOWY_PL00000000000000000000000000.csv";

        await useCase.ExecuteAsync(
            new ImportRequest(CsvStatementInputFactory.FromCsv(IdenticalRowsCsv, fileName), accountId),
            null,
            CancellationToken.None);

        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().NotContain(m => m.Contains("JAN_TESTOWY", StringComparison.Ordinal)
            || m.Contains("PL00000000000000000000000000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Execute_AgainstUnknownAccount_Throws()
    {
        await using (var db = _factory.CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        var useCase = CreateUseCase();

        var input = CsvStatementInputFactory.FromGoldenFile();
        var act = async () => await useCase.ExecuteAsync(
            new ImportRequest(input, Guid.NewGuid()), null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Execute_ReportsStagesInOrder()
    {
        var accountId = await SeedAccountAsync();
        var useCase = CreateUseCase();
        var recorder = new ProgressRecorder();

        var input = CsvStatementInputFactory.FromGoldenFile();
        await useCase.ExecuteAsync(new ImportRequest(input, accountId), recorder, CancellationToken.None);

        recorder.Stages.Should().Equal(
            ImportStage.ReadingFile,
            ImportStage.DetectingBank,
            ImportStage.Parsing,
            ImportStage.Deduplicating,
            ImportStage.Categorizing,
            ImportStage.Saving);
    }

    [Fact]
    public async Task Execute_CategorisesRowsViaSeededRule()
    {
        var accountId = await SeedAccountAsync();
        var categoryId = await SeedCategoryWithRuleAsync(pattern: ".");
        var useCase = CreateUseCase();

        var input = CsvStatementInputFactory.FromGoldenFile();
        var summary = await useCase.ExecuteAsync(new ImportRequest(input, accountId), null, CancellationToken.None);

        summary.Added.Should().Be(8);
        summary.Categorized.Should().Be(8, "a catch-all rule categorises every added row at import time");

        await using var db = _factory.CreateDbContext();
        (await db.Transactions.AllAsync(t => t.CategoryId == categoryId)).Should().BeTrue();
        (await db.CategoryCache.CountAsync())
            .Should().BeGreaterThan(0, "rule hits are written back to the cache during import");
    }

    [Fact]
    public async Task Execute_DropsAccountConfirmationWarning()
    {
        var accountId = await SeedAccountAsync();
        var useCase = CreateUseCase();

        var input = CsvStatementInputFactory.FromGoldenFile();
        var summary = await useCase.ExecuteAsync(new ImportRequest(input, accountId), null, CancellationToken.None);

        // The parser warns the PKO CSV omits the account number, but this flow always
        // confirms the target account, so the warning must not reach the user-facing summary.
        summary.Warnings.Should().NotContain(PkoHistoriaCsvParser.AccountNumberAbsentWarning);
    }

    [Fact]
    public async Task Execute_AiFallbackResult_SetsFlagsAndSuppressesRawAiWarnings()
    {
        var accountId = await SeedAccountAsync();
        // Registry with no deterministic parser but an AI fallback, so any statement resolves to it.
        var registry = new StatementParserRegistry([], new StubAiFallbackParser());
        var categorizer = new RuleCacheCategorizer(_factory, new RuleEngine(NullLogger<RuleEngine>.Instance));
        var useCase = new ImportStatementUseCase(
            _factory, new FingerprintBankDetector(), registry, categorizer, NullLogger<ImportStatementUseCase>.Instance);

        var input = CsvStatementInputFactory.FromGoldenFile();
        var summary = await useCase.ExecuteAsync(new ImportRequest(input, accountId), null, CancellationToken.None);

        summary.AiFallbackUsed.Should().BeTrue();
        summary.OwnerNameUnredacted.Should().BeTrue("the stub result carries the owner-name-unset warning");
        summary.Warnings.Should().NotContain(AiAssistedParser.ReviewWarning);
        summary.Warnings.Should().NotContain(AiAssistedParser.OwnerNameUnsetWarning);
        summary.Warnings.Should().NotContain(AiAssistedParser.AccountNumberAbsentWarning);
        summary.Warnings.Should().Contain("2 rows looked odd.", "unrelated parser warnings still reach the user");
    }

    private ImportStatementUseCase CreateUseCase()
    {
        var registry = new StatementParserRegistry([new PkoHistoriaCsvParser()]);
        var categorizer = new RuleCacheCategorizer(
            _factory, new RuleEngine(NullLogger<RuleEngine>.Instance));
        return new ImportStatementUseCase(
            _factory,
            new FingerprintBankDetector(),
            registry,
            categorizer,
            NullLogger<ImportStatementUseCase>.Instance);
    }

    private async Task<Guid> SeedCategoryWithRuleAsync(string pattern)
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();

        var category = new Category { Id = Guid.NewGuid(), Name = "Inne", Color = "#636366" };
        db.Categories.Add(category);
        db.Rules.Add(new Rule
        {
            Id = Guid.NewGuid(),
            Pattern = pattern,
            Priority = 100,
            CategoryId = category.Id,
            IsEnabled = true,
        });
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task<Guid> SeedAccountAsync()
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = "PKO checking",
            BankCode = "PKO_BP",
            AccountNumber = AccountNumber,
            Currency = "PLN",
            Type = AccountType.Checking,
            CreatedAt = DateTime.UtcNow,
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private sealed class RecordingLogger : ILogger<ImportStatementUseCase>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class ProgressRecorder : IProgress<ImportProgress>
    {
        public List<ImportStage> Stages { get; } = [];

        public void Report(ImportProgress value) => Stages.Add(value.Stage);
    }

    // Mimics the AI fallback's output shape without any AI plumbing: it carries the same
    // warning constants and bank code so the use case's flag/suppression logic can be checked.
    private sealed class StubAiFallbackParser : IStatementParser
    {
        public string BankCode => AiAssistedParser.AiFallbackBankCode;

        public StatementFormat Format => StatementFormat.Pdf;

        public bool CanHandle(BankFingerprint fingerprint) => true;

        public Task<ParseResult> ParseAsync(StatementInput input, CancellationToken ct)
        {
            var transactions = new List<ParsedTransaction>
            {
                new(new DateOnly(2026, 1, 5), null, -49.99m, "PLN", "BIEDRONKA 1234", "BIEDRONKA"),
                new(new DateOnly(2026, 1, 10), null, 5000.00m, "PLN", "WYNAGRODZENIE", null),
            };
            var warnings = new List<string>
            {
                AiAssistedParser.ReviewWarning,
                AiAssistedParser.AccountNumberAbsentWarning,
                AiAssistedParser.OwnerNameUnsetWarning,
                "2 rows looked odd.",
            };
            return Task.FromResult(new ParseResult(
                AiAssistedParser.AiFallbackBankCode,
                AccountNumber: string.Empty,
                Currency: "PLN",
                PeriodFrom: new DateOnly(2026, 1, 1),
                PeriodTo: new DateOnly(2026, 1, 31),
                Transactions: transactions,
                Confidence: ParserConfidence.Medium,
                Warnings: warnings));
        }
    }
}
