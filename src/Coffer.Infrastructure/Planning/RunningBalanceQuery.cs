using Coffer.Core.Planning;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Planning;

/// <summary>
/// Opening balance = the running sum of every PLN transaction up to and including the as-of date.
/// Accurate only while the imported statement history is contiguous; gaps are surfaced separately by
/// <see cref="IStatementContinuityChecker"/> so the planner can warn the owner.
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

    public async Task<decimal> GetBalanceAsOfAsync(DateOnly asOf, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Transactions.AsNoTracking()
            .Where(t => t.Currency == Currency && t.Date <= asOf)
            .SumAsync(t => (decimal?)t.Amount, ct)
            .ConfigureAwait(false) ?? 0m;
    }
}
