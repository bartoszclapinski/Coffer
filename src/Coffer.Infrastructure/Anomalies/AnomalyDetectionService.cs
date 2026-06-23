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
/// free — no AI runs here (that is 13-B).
/// </summary>
public sealed class AnomalyDetectionService : IDetectAnomaliesUseCase
{
    private const int _recentWindowDays = 30;
    private const int _baselineMonths = 6;

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;
    private readonly IReadOnlyList<IAnomalyDetector> _detectors;
    private readonly ILogger<AnomalyDetectionService> _logger;

    public AnomalyDetectionService(
        IDbContextFactory<CofferDbContext> contextFactory,
        IEnumerable<IAnomalyDetector> detectors,
        ILogger<AnomalyDetectionService> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(detectors);
        ArgumentNullException.ThrowIfNull(logger);
        _contextFactory = contextFactory;
        _detectors = detectors.ToList();
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
        var recentFrom = latest.AddDays(-(_recentWindowDays - 1));
        var baselineFrom = recentFrom.AddMonths(-_baselineMonths);

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

        var now = DateTime.UtcNow;
        var added = 0;
        foreach (var candidate in candidates.Where(c => !existingSet.Contains(c.Signature)))
        {
            db.Alerts.Add(new Alert
            {
                Id = Guid.NewGuid(),
                DetectedAt = now,
                Type = candidate.Type,
                Signature = candidate.Signature,
                Title = candidate.Title,
                Description = candidate.Description,
                Status = AlertStatus.New,
                RelatedAmount = candidate.RelatedAmount,
                RelatedTransactionId = candidate.RelatedTransactionId,
                PeriodFrom = candidate.PeriodFrom,
                PeriodTo = candidate.PeriodTo,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Anomaly scan raised {Count} new alert(s).", added);
        }

        return added;
    }
}
