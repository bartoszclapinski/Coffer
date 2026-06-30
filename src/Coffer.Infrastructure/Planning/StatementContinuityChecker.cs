using Coffer.Core.Planning;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Planning;

/// <summary>
/// Detects gaps between imported statement periods per account. The covered periods come from each
/// transaction's <c>ImportSession</c> (its <c>PeriodFrom</c>/<c>PeriodTo</c>); within an account they
/// are merged in date order and any uncovered stretch &gt; 1 day becomes a <see cref="StatementGap"/>.
/// Overlapping periods are not gaps.
/// </summary>
public sealed class StatementContinuityChecker : IStatementContinuityChecker
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public StatementContinuityChecker(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<StatementGap>> FindGapsAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var periods = await db.Transactions.AsNoTracking()
            .Select(t => new { t.AccountId, t.ImportSession.PeriodFrom, t.ImportSession.PeriodTo })
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var gaps = new List<StatementGap>();

        foreach (var account in periods.GroupBy(p => p.AccountId))
        {
            var ordered = account
                .OrderBy(p => p.PeriodFrom)
                .ThenBy(p => p.PeriodTo)
                .ToList();

            var coveredTo = ordered[0].PeriodTo;
            foreach (var period in ordered.Skip(1))
            {
                if (period.PeriodFrom > coveredTo.AddDays(1))
                {
                    gaps.Add(new StatementGap(account.Key, coveredTo.AddDays(1), period.PeriodFrom.AddDays(-1)));
                }

                if (period.PeriodTo > coveredTo)
                {
                    coveredTo = period.PeriodTo;
                }
            }
        }

        return gaps;
    }
}
