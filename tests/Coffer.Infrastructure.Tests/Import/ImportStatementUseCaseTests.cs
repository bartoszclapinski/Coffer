using System.Security.Cryptography;
using Coffer.Core.Domain;
using Coffer.Core.Import;
using Coffer.Core.Parsing;
using Coffer.Infrastructure.Categorization;
using Coffer.Infrastructure.Import;
using Coffer.Infrastructure.Parsing;
using Coffer.Infrastructure.Parsing.Ai;
using Coffer.Infrastructure.Parsing.Pko;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Tests.Parsing.Pko;
using Coffer.Infrastructure.Tests.Persistence;
using Coffer.Shared.Parsing;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Import;

public class ImportStatementUseCaseTests : IDisposable
{
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
            AccountNumber = "PL60102010260000042270201111",
            Currency = "PLN",
            Type = AccountType.Checking,
            CreatedAt = DateTime.UtcNow,
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
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
