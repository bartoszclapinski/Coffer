using Coffer.Core.Planning;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Planning;

/// <summary>
/// <see cref="IVariableBurnQuery"/> over the transaction history. Averages the magnitude of
/// discretionary outflows across a trailing window (default 3 months) into a per-day rate, excluding any
/// outflow already attributable to an active <see cref="Core.Domain.RecurringFlow"/> — matched by
/// merchant key or category — so the recurring instalments/taxes the engine projects explicitly are not
/// double-counted in the burn overlay. Server-side aggregation; returns <c>0</c> when the window holds
/// no qualifying spend.
/// </summary>
public sealed class VariableBurnQuery : IVariableBurnQuery
{
    private const string Currency = "PLN";
    private const int TrailingMonths = 3;

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public VariableBurnQuery(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<decimal> GetDailyBurnAsync(Guid? accountId, DateOnly asOf, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var windowStart = asOf.AddMonths(-TrailingMonths);
        var windowDays = asOf.DayNumber - windowStart.DayNumber;
        if (windowDays <= 0)
        {
            return 0m;
        }

        // Active recurring flows are already projected explicitly, so their historical instances must not
        // be counted as ordinary burn. Exclude by merchant key and by category.
        var activeFlows = await db.RecurringFlows.AsNoTracking()
            .Where(f => f.IsActive)
            .Select(f => new { f.MatchMerchant, f.MatchCategoryId })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var excludedMerchants = activeFlows
            .Select(f => f.MatchMerchant)
            .Where(m => m != null)
            .Select(m => m!)
            .Distinct()
            .ToList();
        var excludedCategories = activeFlows
            .Where(f => f.MatchCategoryId is not null)
            .Select(f => f.MatchCategoryId!.Value)
            .Distinct()
            .ToList();

        var query = db.Transactions.AsNoTracking()
            .Where(t => t.Currency == Currency
                && t.Amount < 0m
                && t.Date > windowStart
                && t.Date <= asOf);

        if (accountId is Guid id)
        {
            query = query.Where(t => t.AccountId == id);
        }

        if (excludedMerchants.Count > 0)
        {
            query = query.Where(t => t.Merchant == null || !excludedMerchants.Contains(t.Merchant));
        }

        if (excludedCategories.Count > 0)
        {
            query = query.Where(t => t.CategoryId == null || !excludedCategories.Contains(t.CategoryId.Value));
        }

        var outflowMagnitude = await query
            .SumAsync(t => (decimal?)-t.Amount, ct)
            .ConfigureAwait(false) ?? 0m;

        if (outflowMagnitude <= 0m)
        {
            return 0m;
        }

        return Math.Round(outflowMagnitude / windowDays, 2, MidpointRounding.AwayFromZero);
    }
}
