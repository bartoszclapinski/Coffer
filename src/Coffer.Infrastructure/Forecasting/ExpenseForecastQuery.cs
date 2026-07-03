using Coffer.Core.Domain;
using Coffer.Core.Forecasting;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Forecasting;

/// <summary>
/// <see cref="IExpenseForecastQuery"/> over the transaction history, recurring flows, and budgets. Anchors
/// on the current month (the latest transaction's month, like the dashboard), targets the next calendar
/// month, and assembles each category's inputs for <see cref="ExpenseForecastEngine"/>:
/// <list type="bullet">
/// <item>the <b>fixed</b> part — active <see cref="FlowDirection.Outflow"/> flows attributed by
/// <see cref="RecurringFlow.MatchCategoryId"/> whose cadence lands in the target month;</item>
/// <item>the <b>variable</b> part — trailing-<see cref="TrailingMonths"/>-month per-category debit
/// magnitude, excluding transactions whose merchant matches an active flow (so the recurring portion is
/// not double-counted), averaged to a monthly figure;</item>
/// <item>the <b>current limit</b> — the category's active budget, if any.</item>
/// </list>
/// The engine calculates; this only assembles. Nothing here calls AI.
/// </summary>
public sealed class ExpenseForecastQuery : IExpenseForecastQuery
{
    private const string Currency = "PLN";
    private const int TrailingMonths = 3;

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;
    private readonly ExpenseForecastEngine _engine;

    public ExpenseForecastQuery(IDbContextFactory<CofferDbContext> contextFactory, ExpenseForecastEngine engine)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(engine);
        _contextFactory = contextFactory;
        _engine = engine;
    }

    public async Task<ExpenseForecast> GetForecastAsync(Guid? accountId, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var scope = db.Transactions.AsNoTracking().Where(t => t.Currency == Currency);
        if (accountId is { } id)
        {
            scope = scope.Where(t => t.AccountId == id);
        }

        var hasData = await scope.AnyAsync(ct).ConfigureAwait(false);
        var asOf = hasData
            ? await scope.MaxAsync(t => t.Date, ct).ConfigureAwait(false)
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var anchorMonthStart = new DateOnly(asOf.Year, asOf.Month, 1);
        var targetMonthStart = anchorMonthStart.AddMonths(1);

        // --- Fixed: active outflow flows that land in the target month, attributed by category. ---
        var activeFlows = await db.RecurringFlows.AsNoTracking()
            .Where(f => f.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var fixedByCategory = new Dictionary<Guid, decimal>();
        var fixedUnattributed = 0m;
        foreach (var flow in activeFlows)
        {
            if (flow.Direction != FlowDirection.Outflow || !FlowSchedule.OccursInMonth(flow, targetMonthStart))
            {
                continue;
            }

            if (flow.MatchCategoryId is { } categoryId)
            {
                fixedByCategory[categoryId] = fixedByCategory.GetValueOrDefault(categoryId) + flow.TypicalAmount;
            }
            else
            {
                fixedUnattributed += flow.TypicalAmount;
            }
        }

        // --- Variable: trailing-window per-category debits, excluding active-flow merchants, per month. ---
        var excludedMerchants = activeFlows
            .Select(f => f.MatchMerchant)
            .Where(m => m != null)
            .Select(m => m!)
            .Distinct()
            .ToList();

        var windowStart = asOf.AddMonths(-TrailingMonths);
        var variableQuery = scope.Where(t => t.Amount < 0m && t.Date > windowStart && t.Date <= asOf);
        if (excludedMerchants.Count > 0)
        {
            variableQuery = variableQuery.Where(t => t.Merchant == null || !excludedMerchants.Contains(t.Merchant));
        }

        var variableByCategoryRaw = await variableQuery
            .GroupBy(t => t.CategoryId)
            .Select(g => new { g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var variableByCategory = new Dictionary<Guid, decimal>();
        var variableUncategorised = 0m;
        foreach (var row in variableByCategoryRaw)
        {
            var monthly = Math.Round(-row.Total / TrailingMonths, 2, MidpointRounding.AwayFromZero);
            if (row.Key is { } categoryId)
            {
                variableByCategory[categoryId] = monthly;
            }
            else
            {
                variableUncategorised = monthly;
            }
        }

        // --- Current budget limits (active, PLN). ---
        var budgets = await db.CategoryBudgets.AsNoTracking()
            .Where(b => b.IsActive && b.Currency == Currency)
            .Select(b => new { b.CategoryId, b.LimitAmount })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var limitByCategory = budgets.ToDictionary(b => b.CategoryId, b => b.LimitAmount);

        // --- Assemble per-category inputs (names/colours for the real categories involved). ---
        var involvedIds = fixedByCategory.Keys.Union(variableByCategory.Keys).ToList();
        var categoryMeta = await db.Categories.AsNoTracking()
            .Where(c => involvedIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Color })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var metaById = categoryMeta.ToDictionary(c => c.Id);

        var inputs = new List<CategoryForecastInput>();
        foreach (var categoryId in involvedIds)
        {
            var meta = metaById.GetValueOrDefault(categoryId);
            inputs.Add(new CategoryForecastInput(
                categoryId,
                meta?.Name,
                meta?.Color,
                fixedByCategory.GetValueOrDefault(categoryId),
                variableByCategory.GetValueOrDefault(categoryId),
                limitByCategory.TryGetValue(categoryId, out var limit) ? limit : null));
        }

        if (fixedUnattributed > 0m || variableUncategorised > 0m)
        {
            inputs.Add(new CategoryForecastInput(null, null, null, fixedUnattributed, variableUncategorised, null));
        }

        return _engine.Forecast(targetMonthStart, inputs);
    }
}
