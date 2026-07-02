using Coffer.Core.Budgeting;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Budgeting;

/// <summary>
/// Read-side budget tracking. Anchors on the current month like the dashboard (the latest transaction's
/// month, so an idle current month is not empty), sums each category's month-to-date debits server-side
/// (positive magnitudes, optionally per account), runs each budgeted category through
/// <see cref="BudgetTrackingEngine"/>, and lists the rest — including the uncategorised bucket — as
/// unbudgeted so category-less spend is visible, not hidden.
/// </summary>
public sealed class BudgetTrackingQuery : IBudgetTrackingQuery
{
    private const string DisplayCurrency = "PLN";

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;
    private readonly BudgetTrackingEngine _engine;

    public BudgetTrackingQuery(IDbContextFactory<CofferDbContext> contextFactory, BudgetTrackingEngine engine)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(engine);
        _contextFactory = contextFactory;
        _engine = engine;
    }

    public async Task<BudgetOverview> GetOverviewAsync(Guid? accountId, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var scope = db.Transactions.AsNoTracking().Where(t => t.Currency == DisplayCurrency);
        if (accountId is { } id)
        {
            scope = scope.Where(t => t.AccountId == id);
        }

        var hasData = await scope.AnyAsync(ct).ConfigureAwait(false);
        var asOf = hasData
            ? await scope.MaxAsync(t => t.Date, ct).ConfigureAwait(false)
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(asOf.Year, asOf.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        var daysInMonth = DateTime.DaysInMonth(asOf.Year, asOf.Month);
        var daysElapsed = asOf.Day;

        var byCategory = await scope
            .Where(t => t.Amount < 0 && t.Date >= monthStart && t.Date < nextMonthStart)
            .GroupBy(t => t.CategoryId)
            .Select(g => new { g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var spentByCategory = byCategory
            .Where(x => x.Key != null)
            .ToDictionary(x => x.Key!.Value, x => -x.Total);
        var uncategorisedSpent = byCategory.Where(x => x.Key == null).Sum(x => -x.Total);

        var budgets = await db.CategoryBudgets.AsNoTracking()
            .Where(b => b.IsActive && b.Currency == DisplayCurrency)
            .Join(
                db.Categories,
                b => b.CategoryId,
                c => c.Id,
                (b, c) => new { b.CategoryId, c.Name, c.Color, b.LimitAmount })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var budgetedIds = budgets.Select(b => b.CategoryId).ToHashSet();

        var budgetLines = budgets
            .Select(b =>
            {
                var spent = spentByCategory.TryGetValue(b.CategoryId, out var s) ? s : 0m;
                var status = _engine.Evaluate(b.LimitAmount, spent, daysElapsed, daysInMonth);
                return new BudgetLine(b.CategoryId, b.Name, b.Color, status);
            })
            .OrderByDescending(l => l.Status.Fraction)
            .ThenByDescending(l => l.Status.Spent)
            .ToList();

        var spentCategoryIds = spentByCategory.Keys.ToList();
        var categoryMeta = await db.Categories.AsNoTracking()
            .Where(c => spentCategoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Color })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var unbudgeted = new List<UnbudgetedLine>();
        foreach (var (categoryId, spent) in spentByCategory)
        {
            if (budgetedIds.Contains(categoryId))
            {
                continue;
            }

            var meta = categoryMeta.FirstOrDefault(c => c.Id == categoryId);
            unbudgeted.Add(new UnbudgetedLine(categoryId, meta?.Name, meta?.Color, spent));
        }

        if (uncategorisedSpent > 0m)
        {
            unbudgeted.Add(new UnbudgetedLine(null, null, null, uncategorisedSpent));
        }

        return new BudgetOverview(monthStart, budgetLines, [.. unbudgeted.OrderByDescending(u => u.Spent)]);
    }
}
