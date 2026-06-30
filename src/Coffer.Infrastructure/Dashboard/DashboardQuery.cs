using Coffer.Core.Dashboard;
using Coffer.Core.Domain;
using Coffer.Core.Transactions;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Dashboard;

/// <summary>
/// Read-side dashboard aggregation over <c>Transactions</c>. Everything is computed
/// server-side (SUM / GROUP BY, <c>AsNoTracking</c>) and projected into the
/// <see cref="DashboardSnapshot"/> DTOs — no entities are materialised for the
/// aggregates. Figures are scoped to a single display currency (PLN by default; the
/// multi-currency split is a later phase) and optionally to one account. Spend is the
/// positive magnitude of debits (negative amounts); income is the sum of credits.
/// </summary>
public sealed class DashboardQuery : IDashboardQuery
{
    private const int TrendDays = 30;
    private const int TrendMonths = 12;
    private const int TopCategories = 6;
    private const int RecentCount = 8;

    private const string UncategorizedName = "Bez kategorii";
    private const string UncategorizedColor = "#8E8E93";
    private const string RemainderName = "Pozostałe";
    private const string RemainderColor = "#C7C7CC";

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public DashboardQuery(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(DashboardFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var scope = db.Transactions.AsNoTracking().Where(t => t.Currency == filter.Currency);
        if (filter.AccountId is { } accountId)
        {
            scope = scope.Where(t => t.AccountId == accountId);
        }

        var hasData = await scope.AnyAsync(ct).ConfigureAwait(false);

        // With no explicit as-of, anchor on the latest transaction in scope rather than the
        // wall clock: an idle current month would otherwise leave the KPIs and doughnut empty
        // even when recent data exists. Falls back to today only for an empty scope.
        var today = filter.AsOf
            ?? (hasData
                ? await scope.MaxAsync(t => t.Date, ct).ConfigureAwait(false)
                : DateOnly.FromDateTime(DateTime.UtcNow));
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);

        var summary = await GetCurrentMonthSummaryAsync(scope, monthStart, nextMonthStart, filter.Currency, ct)
            .ConfigureAwait(false);
        var categories = await GetTopCategoriesAsync(db, scope, monthStart, nextMonthStart, ct)
            .ConfigureAwait(false);
        var daily = await GetDailyTrendAsync(scope, today, ct).ConfigureAwait(false);
        var monthly = await GetMonthlyTrendAsync(scope, monthStart, nextMonthStart, ct).ConfigureAwait(false);
        var recent = await GetRecentTransactionsAsync(scope, ct).ConfigureAwait(false);

        return new DashboardSnapshot(summary, categories, daily, monthly, recent, hasData);
    }

    private static async Task<MonthlySummary> GetCurrentMonthSummaryAsync(
        IQueryable<Transaction> scope,
        DateOnly monthStart,
        DateOnly nextMonthStart,
        string currency,
        CancellationToken ct)
    {
        var month = scope.Where(t => t.Date >= monthStart && t.Date < nextMonthStart);

        var debitSum = await month.Where(t => t.Amount < 0)
            .SumAsync(t => (decimal?)t.Amount, ct).ConfigureAwait(false) ?? 0m;
        var income = await month.Where(t => t.Amount > 0)
            .SumAsync(t => (decimal?)t.Amount, ct).ConfigureAwait(false) ?? 0m;
        var count = await month.CountAsync(ct).ConfigureAwait(false);

        var spend = -debitSum;
        return new MonthlySummary(monthStart, spend, income, income - spend, currency, count);
    }

    private static async Task<IReadOnlyList<CategorySlice>> GetTopCategoriesAsync(
        CofferDbContext db,
        IQueryable<Transaction> scope,
        DateOnly monthStart,
        DateOnly nextMonthStart,
        CancellationToken ct)
    {
        var totals = await scope
            .Where(t => t.Date >= monthStart && t.Date < nextMonthStart && t.Amount < 0)
            .GroupBy(t => t.CategoryId)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (totals.Count == 0)
        {
            return [];
        }

        var ids = totals.Where(t => t.CategoryId != null).Select(t => t.CategoryId!.Value).ToList();
        var names = await db.Categories.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Color })
            .ToDictionaryAsync(c => c.Id, c => (c.Name, c.Color), ct)
            .ConfigureAwait(false);

        var grandTotal = totals.Sum(t => -t.Total);
        if (grandTotal <= 0m)
        {
            return [];
        }

        var ranked = totals
            .Select(t =>
            {
                var magnitude = -t.Total;
                var (name, color) = t.CategoryId is { } id && names.TryGetValue(id, out var meta)
                    ? meta
                    : (UncategorizedName, UncategorizedColor);
                return new CategorySlice(t.CategoryId, name, color, magnitude, (double)(magnitude / grandTotal));
            })
            .OrderByDescending(s => s.Total)
            .ToList();

        if (ranked.Count <= TopCategories)
        {
            return ranked;
        }

        var top = ranked.Take(TopCategories).ToList();
        var remainderTotal = ranked.Skip(TopCategories).Sum(s => s.Total);
        top.Add(new CategorySlice(
            null, RemainderName, RemainderColor, remainderTotal, (double)(remainderTotal / grandTotal)));
        return top;
    }

    private static async Task<IReadOnlyList<TrendPoint>> GetDailyTrendAsync(
        IQueryable<Transaction> scope,
        DateOnly today,
        CancellationToken ct)
    {
        var start = today.AddDays(-(TrendDays - 1));

        var byDay = await scope
            .Where(t => t.Amount < 0 && t.Date >= start && t.Date <= today)
            .GroupBy(t => t.Date)
            .Select(g => new { Date = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var lookup = byDay.ToDictionary(d => d.Date, d => -d.Total);
        var points = new List<TrendPoint>(TrendDays);
        for (var i = 0; i < TrendDays; i++)
        {
            var day = start.AddDays(i);
            points.Add(new TrendPoint(day, lookup.TryGetValue(day, out var total) ? total : 0m));
        }

        return points;
    }

    private static async Task<IReadOnlyList<TrendPoint>> GetMonthlyTrendAsync(
        IQueryable<Transaction> scope,
        DateOnly monthStart,
        DateOnly nextMonthStart,
        CancellationToken ct)
    {
        var start = monthStart.AddMonths(-(TrendMonths - 1));

        var byMonth = await scope
            .Where(t => t.Amount < 0 && t.Date >= start && t.Date < nextMonthStart)
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var lookup = byMonth.ToDictionary(m => (m.Year, m.Month), m => -m.Total);
        var points = new List<TrendPoint>(TrendMonths);
        for (var i = 0; i < TrendMonths; i++)
        {
            var bucket = start.AddMonths(i);
            points.Add(new TrendPoint(
                bucket, lookup.TryGetValue((bucket.Year, bucket.Month), out var total) ? total : 0m));
        }

        return points;
    }

    private static async Task<IReadOnlyList<TransactionListItem>> GetRecentTransactionsAsync(
        IQueryable<Transaction> scope,
        CancellationToken ct) =>
        await scope
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .Take(RecentCount)
            .Select(t => new TransactionListItem(
                t.Id,
                t.Date,
                t.Description,
                t.Merchant,
                t.Amount,
                t.Currency,
                t.AccountId,
                t.Account.Name,
                t.CategoryId,
                t.Category != null ? t.Category.Name : null,
                t.Category != null ? t.Category.Color : null))
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
