using Coffer.Core.Planning;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Planning;

/// <summary>
/// Resolves the balance dependency window for an account — anchor date if set, else the earliest
/// imported transaction — and reports any <see cref="StatementGap"/> from
/// <see cref="IStatementContinuityChecker"/> that overlaps it up to the as-of date. Reuses the existing
/// continuity checker rather than re-deriving gap logic.
/// </summary>
public sealed class BalanceTrustQuery : IBalanceTrustQuery
{
    private const string Currency = "PLN";

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;
    private readonly IStatementContinuityChecker _continuityChecker;

    public BalanceTrustQuery(
        IDbContextFactory<CofferDbContext> contextFactory,
        IStatementContinuityChecker continuityChecker)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(continuityChecker);
        _contextFactory = contextFactory;
        _continuityChecker = continuityChecker;
    }

    public async Task<BalanceTrust> CheckAsync(Guid accountId, DateOnly asOf, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var anchorDate = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.AnchorDate)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        DateOnly windowFrom;
        if (anchorDate is DateOnly anchor)
        {
            windowFrom = anchor;
        }
        else
        {
            var earliest = await db.Transactions.AsNoTracking()
                .Where(t => t.AccountId == accountId && t.Currency == Currency)
                .Select(t => (DateOnly?)t.Date)
                .MinAsync(ct)
                .ConfigureAwait(false);

            // No transactions and no anchor: there is no window to distrust.
            if (earliest is not DateOnly first)
            {
                return new BalanceTrust(IsTrustworthy: true, WindowFrom: asOf, Gaps: Array.Empty<StatementGap>());
            }

            windowFrom = first;
        }

        var gaps = await _continuityChecker.FindGapsAsync(ct).ConfigureAwait(false);

        // A gap corrupts the balance only when it overlaps the [windowFrom, asOf] dependency window
        // for this account. Inclusive ranges, so overlap is From <= asOf && To >= windowFrom.
        var offending = gaps
            .Where(g => g.AccountId == accountId && g.From <= asOf && g.To >= windowFrom)
            .ToList();

        return new BalanceTrust(offending.Count == 0, windowFrom, offending);
    }
}
