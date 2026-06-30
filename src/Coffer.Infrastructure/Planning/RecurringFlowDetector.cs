using Coffer.Core.Domain;
using Coffer.Core.Planning;
using Coffer.Infrastructure.Analysis;
using Coffer.Infrastructure.Anomalies;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Planning;

/// <summary>
/// Proposes recurring flows from the PLN transaction history: merchants seen across enough distinct
/// months (<see cref="AnomalyThresholds.MinRecurrenceMonths"/>) become candidates with an inferred
/// anchor day (median), a typical amount and its spread, and a direction taken from the sign of the
/// charges. v1 proposes everything as monthly (<c>IntervalMonths = 1</c>); quarterly/yearly cadence
/// and the accrual offset are left to the owner, who confirms each suggestion. Detection only
/// suggests — it never writes.
/// </summary>
public sealed class RecurringFlowDetector : IRecurringFlowDetector
{
    private const string _currency = "PLN";
    private const int _historyMonths = 12;

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public RecurringFlowDetector(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<RecurringFlowCandidate>> DetectAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var scope = db.Transactions.AsNoTracking()
            .Where(t => t.Currency == _currency && t.Merchant != null && t.Merchant != "");
        if (!await scope.AnyAsync(ct).ConfigureAwait(false))
        {
            return [];
        }

        var latest = await scope.MaxAsync(t => t.Date, ct).ConfigureAwait(false);
        var from = new DateOnly(latest.Year, latest.Month, 1).AddMonths(-_historyMonths);

        var rows = await scope
            .Where(t => t.Date >= from)
            .Select(t => new { t.Date, t.Amount, t.Merchant, t.CategoryId })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var candidates = new List<RecurringFlowCandidate>();

        foreach (var group in rows.GroupBy(r => AnomalyFormatting.MerchantKey(r.Merchant!)))
        {
            var items = group.ToList();
            var dates = items.Select(i => i.Date).ToList();
            var months = RecurrenceStatistics.DistinctMonths(dates);
            if (months < AnomalyThresholds.MinRecurrenceMonths)
            {
                continue;
            }

            var direction = items.Count(i => i.Amount < 0m) >= items.Count(i => i.Amount > 0m)
                ? FlowDirection.Outflow
                : FlowDirection.Inflow;

            var magnitudes = items.Select(i => Math.Abs(i.Amount)).ToList();
            var name = items[^1].Merchant!.Trim();
            var categoryId = items
                .Select(i => i.CategoryId)
                .GroupBy(c => c)
                .OrderByDescending(g => g.Count())
                .First().Key;

            candidates.Add(new RecurringFlowCandidate(
                name,
                direction,
                AnomalyFormatting.MerchantKey(name),
                categoryId,
                IntervalMonths: 1,
                AnchorDayOfMonth: RecurrenceStatistics.MedianDayOfMonth(dates),
                AnchorMonth: null,
                TypicalAmount: magnitudes.Average(),
                AmountStdDev: SampleStdDev(magnitudes),
                MonthsObserved: months));
        }

        return candidates.OrderByDescending(c => c.TypicalAmount).ToList();
    }

    /// <summary>Sample (n-1) standard deviation; the square root drops to <c>double</c> like the other engines.</summary>
    private static decimal SampleStdDev(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2)
        {
            return 0m;
        }

        var mean = values.Average();
        var sumSquares = values.Sum(v => (v - mean) * (v - mean));
        return (decimal)Math.Sqrt((double)(sumSquares / (values.Count - 1)));
    }
}
