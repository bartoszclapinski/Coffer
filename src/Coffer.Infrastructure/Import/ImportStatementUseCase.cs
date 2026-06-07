using System.Security.Cryptography;
using Coffer.Core.Categorization;
using Coffer.Core.Domain;
using Coffer.Core.Import;
using Coffer.Core.Parsing;
using Coffer.Infrastructure.Parsing;
using Coffer.Infrastructure.Parsing.Polish;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Import;

/// <summary>
/// Drives the statement import pipeline: read → detect bank → parse → dedup → save.
/// New transactions and the <see cref="ImportSession"/> that groups them are
/// persisted in a single database transaction. Dedup is by <see cref="Transaction.Hash"/>,
/// which is scoped to the chosen account's number (the PKO CSV omits it), so the
/// same statement re-imported into the same account adds nothing.
/// </summary>
public sealed class ImportStatementUseCase : IImportStatementUseCase
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;
    private readonly IBankDetector _detector;
    private readonly StatementParserRegistry _registry;
    private readonly ICategorizer _categorizer;
    private readonly ILogger<ImportStatementUseCase> _logger;

    public ImportStatementUseCase(
        IDbContextFactory<CofferDbContext> contextFactory,
        IBankDetector detector,
        StatementParserRegistry registry,
        ICategorizer categorizer,
        ILogger<ImportStatementUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(categorizer);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _detector = detector;
        _registry = registry;
        _categorizer = categorizer;
        _logger = logger;
    }

    public async Task<ImportSummary> ExecuteAsync(
        ImportRequest request,
        IProgress<ImportProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var input = request.Statement;

        progress?.Report(new ImportProgress(ImportStage.ReadingFile));
        var fileHash = ComputeFileHash(input.Content);

        progress?.Report(new ImportProgress(ImportStage.DetectingBank));
        input.Content.Position = 0;
        var fingerprint = _detector.Detect(input);

        progress?.Report(new ImportProgress(ImportStage.Parsing));
        input.Content.Position = 0;
        var parser = _registry.Resolve(fingerprint, input.Format);
        var parsed = await parser.ParseAsync(input, ct).ConfigureAwait(false);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.AccountId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Account {request.AccountId} does not exist.");

        progress?.Report(new ImportProgress(ImportStage.Deduplicating));

        var alreadyImported = await db.ImportSessions
            .AnyAsync(s => s.FileHash == fileHash, ct)
            .ConfigureAwait(false);

        var candidates = new List<Transaction>(parsed.Transactions.Count);
        var seenHashes = new HashSet<string>();
        foreach (var pt in parsed.Transactions)
        {
            var normalized = DescriptionNormalizer.Normalize(pt.Description);
            var hash = TransactionHash.Compute(account.AccountNumber, pt.Date, pt.Amount, normalized);
            if (!seenHashes.Add(hash))
            {
                // Duplicate within this very file (same day/amount/desc) — count once.
                continue;
            }

            candidates.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                Date = pt.Date,
                BookingDate = pt.BookingDate,
                Amount = pt.Amount,
                Currency = pt.Currency,
                Description = pt.Description,
                NormalizedDescription = normalized,
                Merchant = pt.Merchant,
                Hash = hash,
                CreatedAt = DateTime.UtcNow,
            });
        }

        var candidateHashes = candidates.Select(t => t.Hash).ToList();
        var existingHashes = await db.Transactions
            .Where(t => candidateHashes.Contains(t.Hash))
            .Select(t => t.Hash)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var existing = existingHashes.ToHashSet();

        var toAdd = candidates.Where(t => !existing.Contains(t.Hash)).ToList();
        var skipped = parsed.Transactions.Count - toAdd.Count;

        // Categorisation (cache → rules → AI batch) runs over the new rows before they're
        // saved. Cache/rule hits are instant; an AI batch (Phase 10-C) may take longer, so
        // it gets its own progress stage. Reported only when there's something to categorise.
        var categorized = 0;
        if (toAdd.Count > 0)
        {
            progress?.Report(new ImportProgress(ImportStage.Categorizing));
            var categories = await _categorizer
                .CategorizeAsync(toAdd.Select(t => t.NormalizedDescription).ToList(), ct)
                .ConfigureAwait(false);

            foreach (var t in toAdd)
            {
                if (categories.TryGetValue(t.NormalizedDescription, out var categoryId)
                    && categoryId is { } id)
                {
                    t.CategoryId = id;
                    categorized++;
                }
            }
        }

        progress?.Report(new ImportProgress(ImportStage.Saving));

        var session = new ImportSession
        {
            Id = Guid.NewGuid(),
            FileName = input.FileName ?? "",
            FileHash = fileHash,
            BankCode = parsed.BankCode,
            PeriodFrom = parsed.PeriodFrom,
            PeriodTo = parsed.PeriodTo,
            ImportedAt = DateTime.UtcNow,
            TransactionsAdded = toAdd.Count,
            TransactionsSkipped = skipped,
            Status = ImportStatus.Completed,
        };

        foreach (var t in toAdd)
        {
            t.ImportSessionId = session.Id;
        }

        await using (var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            db.ImportSessions.Add(session);
            db.Transactions.AddRange(toAdd);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Imported {Added} new transaction(s), skipped {Skipped} duplicate(s) from {File} into account {Account}",
            toAdd.Count, skipped, session.FileName, account.Id);

        // The PKO CSV omits the account number, so the parser warns it must be confirmed
        // at import time. This flow always confirms it (the user picks the target account),
        // so the warning is moot here — drop it rather than alarm the user on every import.
        var userWarnings = parsed.Warnings
            .Where(w => !string.Equals(w, Parsing.Pko.PkoHistoriaCsvParser.AccountNumberAbsentWarning, StringComparison.Ordinal))
            .ToList();

        return new ImportSummary(session.Id, toAdd.Count, skipped, categorized, alreadyImported, userWarnings);
    }

    private static string ComputeFileHash(Stream content)
    {
        content.Position = 0;
        var hash = SHA256.HashData(content);
        content.Position = 0;
        return Convert.ToHexString(hash);
    }
}
