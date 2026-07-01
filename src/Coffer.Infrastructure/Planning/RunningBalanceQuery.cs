using Coffer.Core.Planning;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Planning;

/// <summary>
/// Opening balance for the <see cref="CashFlowProjectionEngine"/>. When the chosen account has an
/// anchor it is absolute — the reconciled <c>AnchorBalance</c> plus the account's transactions after
/// the anchor date. Otherwise it is the relative running sum of transactions up to the as-of date
/// (cross-account when no account is given). Accurate only while the imported statement history is
/// contiguous; gaps are surfaced separately by <see cref="IStatementContinuityChecker"/> so the planner
/// can warn the owner.
/// </summary>
public sealed class RunningBalanceQuery : IRunningBalanceQuery
{
    private const string Currency = "PLN";

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public RunningBalanceQuery(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<decimal> GetBalanceAsOfAsync(DateOnly asOf, Guid? accountId, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        if (accountId is Guid id)
        {
            var account = await db.Accounts.AsNoTracking()
                .Where(a => a.Id == id)
                .Select(a => new { a.AnchorDate, a.AnchorBalance })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (account?.AnchorDate is DateOnly anchorDate && account.AnchorBalance is decimal anchorBalance)
            {
                var postAnchorDelta = await db.Transactions.AsNoTracking()
                    .Where(t => t.AccountId == id && t.Currency == Currency
                        && t.Date > anchorDate && t.Date <= asOf)
                    .SumAsync(t => (decimal?)t.Amount, ct)
                    .ConfigureAwait(false) ?? 0m;
                return anchorBalance + postAnchorDelta;
            }

            return await db.Transactions.AsNoTracking()
                .Where(t => t.AccountId == id && t.Currency == Currency && t.Date <= asOf)
                .SumAsync(t => (decimal?)t.Amount, ct)
                .ConfigureAwait(false) ?? 0m;
        }

        return await db.Transactions.AsNoTracking()
            .Where(t => t.Currency == Currency && t.Date <= asOf)
            .SumAsync(t => (decimal?)t.Amount, ct)
            .ConfigureAwait(false) ?? 0m;
    }
}
