using Coffer.Core.Anomalies;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Anomalies;

/// <summary>
/// Runs every <see cref="IAnomalyDetector"/> over the PLN history and persists the new findings
/// as <see cref="Alert"/> rows. Idempotent: candidates are deduplicated against existing alerts of
/// any status by <see cref="AnomalyCandidate.Signature"/>, so a dismissed anomaly is never
/// re-raised and a re-scan inserts each logical anomaly at most once. Detection is statistical and
/// free; the optional 13-B <see cref="IAnomalyCommentator"/> rewrites the top findings' text with
/// the LLM, falling back to the deterministic templated text on any failure.
/// </summary>
public sealed class AnomalyDetectionService : IDetectAnomaliesUseCase
{
    private const int RecentWindowDays = 30;
    private const int BaselineMonths = 6;
    private const int CommentaryTopN = 10;

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;
    private readonly IReadOnlyList<IAnomalyDetector> _detectors;
    private readonly IAnomalyCommentator _commentator;
    private readonly ILogger<AnomalyDetectionService> _logger;

    public AnomalyDetectionService(
        IDbContextFactory<CofferDbContext> contextFactory,
        IEnumerable<IAnomalyDetector> detectors,
        IAnomalyCommentator commentator,
        ILogger<AnomalyDetectionService> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(detectors);
        ArgumentNullException.ThrowIfNull(commentator);
        ArgumentNullException.ThrowIfNull(logger);
        _contextFactory = contextFactory;
        _detectors = detectors.ToList();
        _commentator = commentator;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var scope = db.Transactions.AsNoTracking().Where(t => t.Currency == "PLN");
        if (!await scope.AnyAsync(ct).ConfigureAwait(false))
        {
            return 0;
        }

        var latest = await scope.MaxAsync(t => t.Date, ct).ConfigureAwait(false);
        var recentTo = latest;
        var recentFrom = latest.AddDays(-(RecentWindowDays - 1));
        var baselineFrom = recentFrom.AddMonths(-BaselineMonths);

        var snapshots = await scope
            .Where(t => t.Date >= baselineFrom && t.Date <= recentTo)
            .Select(t => new TransactionSnapshot(
                t.Id,
                t.Date,
                t.BookingDate,
                t.Amount,
                t.Merchant,
                t.NormalizedDescription,
                t.CategoryId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var recent = snapshots.Where(t => t.Date >= recentFrom).ToList();
        var baseline = snapshots.Where(t => t.Date < recentFrom).ToList();

        var categoryNames = await db.Categories.AsNoTracking()
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct)
            .ConfigureAwait(false);

        var context = new AnomalyDetectionContext(recent, baseline, categoryNames, recentFrom, recentTo);

        var candidates = _detectors
            .SelectMany(d => d.Detect(context))
            .GroupBy(c => c.Signature, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(c => c.Score).First())
            .ToList();

        if (candidates.Count == 0)
        {
            return 0;
        }

        var signatures = candidates.Select(c => c.Signature).ToList();
        var existing = await db.Alerts.AsNoTracking()
            .Where(a => signatures.Contains(a.Signature))
            .Select(a => a.Signature)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);

        var fresh = candidates.Where(c => !existingSet.Contains(c.Signature)).ToList();
        if (fresh.Count == 0)
        {
            return 0;
        }

        // The highest-ranked findings get LLM-written text; the rest keep their templated text.
        var topN = fresh.OrderByDescending(c => c.Score).Take(CommentaryTopN).ToList();
        var commented = await _commentator.CommentAsync(topN, ct).ConfigureAwait(false);
        var textBySignature = commented.ToDictionary(c => c.Signature, StringComparer.Ordinal);

        var now = DateTime.UtcNow;
        foreach (var candidate in fresh)
        {
            var text = textBySignature.GetValueOrDefault(candidate.Signature, candidate);
            db.Alerts.Add(new Alert
            {
                Id = Guid.NewGuid(),
                DetectedAt = now,
                Type = candidate.Type,
                Signature = candidate.Signature,
                Title = text.Title,
                Description = text.Description,
                Status = AlertStatus.New,
                RelatedAmount = candidate.RelatedAmount,
                RelatedTransactionId = candidate.RelatedTransactionId,
                PeriodFrom = candidate.PeriodFrom,
                PeriodTo = candidate.PeriodTo,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Anomaly scan raised {Count} new alert(s).", fresh.Count);

        return fresh.Count;
    }
}
