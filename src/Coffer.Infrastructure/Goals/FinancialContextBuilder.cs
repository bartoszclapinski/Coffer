using Coffer.Core.Goals;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Goals;

/// <summary>
/// Derives the deterministic <see cref="FinancialContext"/> the engine evaluates goals against from
/// the PLN transaction history (doc 07). Works over the six complete calendar months before the
/// anchor month: monthly income is the average of credits; spend is split into fixed and variable by
/// recurrence (a category billed in at least five of the six months is treated as fixed), and the
/// variable series yields the 6-month average and its spread for risk modelling. Seasonality ships as
/// a neutral 1.0 stub in v1 (the real per-month model is a deferred follow-up). All money stays
/// <c>decimal</c> (hard rule #1); only the standard-deviation's square root drops to <c>double</c>,
/// matching the anomaly engine's convention for statistical spread.
/// </summary>
public sealed class FinancialContextBuilder : IFinancialContextBuilder
{
    private const int _windowMonths = 6;
    private const int _fixedPresenceThreshold = 5;
    private const string _currency = "PLN";
    private const string _uncategorizedName = "Bez kategorii";

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public FinancialContextBuilder(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<FinancialContext> BuildAsync(DateOnly today, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var scope = db.Transactions.AsNoTracking().Where(t => t.Currency == _currency);
        var hasData = await scope.AnyAsync(ct).ConfigureAwait(false);

        // Anchor on the latest transaction rather than the wall clock so a gap since the last import
        // does not zero out income; fall back to today only when there is nothing to anchor on.
        var anchor = hasData
            ? await scope.MaxAsync(t => t.Date, ct).ConfigureAwait(false)
            : today;
        var monthStart = new DateOnly(anchor.Year, anchor.Month, 1);
        var windowStart = monthStart.AddMonths(-_windowMonths);

        var window = scope.Where(t => t.Date >= windowStart && t.Date < monthStart);

        var monthlyIncome = await window.Where(t => t.Amount > 0m)
            .SumAsync(t => (decimal?)t.Amount, ct).ConfigureAwait(false) ?? 0m;
        monthlyIncome /= _windowMonths;

        var debitsByCategoryMonth = await window.Where(t => t.Amount < 0m)
            .GroupBy(t => new { t.CategoryId, t.Date.Year, t.Date.Month })
            .Select(g => new { g.Key.CategoryId, g.Key.Year, g.Key.Month, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var categoryNames = await db.Categories.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct)
            .ConfigureAwait(false);

        // Magnitudes per category, one slot per window month (absent months are zero).
        // Uncategorized debits collapse onto Guid.Empty since a dictionary key cannot be null.
        var perCategory = new Dictionary<Guid, decimal[]>();
        foreach (var row in debitsByCategoryMonth)
        {
            var slot = MonthIndex(windowStart, row.Year, row.Month);
            if (slot < 0 || slot >= _windowMonths)
            {
                continue;
            }

            var key = row.CategoryId ?? Guid.Empty;
            if (!perCategory.TryGetValue(key, out var months))
            {
                months = new decimal[_windowMonths];
                perCategory[key] = months;
            }

            months[slot] += -row.Total;
        }

        var monthlyFixed = 0m;
        var variableByMonth = new decimal[_windowMonths];
        var categoryAverages = new Dictionary<string, decimal>();

        foreach (var (categoryId, months) in perCategory)
        {
            var presence = months.Count(m => m > 0m);
            var average = months.Sum() / _windowMonths;

            var name = categoryId != Guid.Empty && categoryNames.TryGetValue(categoryId, out var n) ? n : _uncategorizedName;
            categoryAverages[name] = average;

            if (presence >= _fixedPresenceThreshold)
            {
                monthlyFixed += average;
            }
            else
            {
                for (var i = 0; i < _windowMonths; i++)
                {
                    variableByMonth[i] += months[i];
                }
            }
        }

        var monthlyVariableAvg = variableByMonth.Sum() / _windowMonths;
        var monthlyVariableStdDev = SampleStdDev(variableByMonth);

        return new FinancialContext
        {
            MonthlyIncome = monthlyIncome,
            MonthlyFixedExpenses = monthlyFixed,
            MonthlyVariableAvg = monthlyVariableAvg,
            MonthlyVariableStdDev = monthlyVariableStdDev,
            OtherActiveGoals = [],
            CategoryAverages6m = categoryAverages,
            SeasonalityModifiers = new Dictionary<int, decimal>(),
            Today = today,
        };
    }

    private static int MonthIndex(DateOnly windowStart, int year, int month) =>
        ((year - windowStart.Year) * 12) + (month - windowStart.Month);

    /// <summary>Sample (n-1) standard deviation; the square root drops to <c>double</c> like the anomaly engine.</summary>
    private static decimal SampleStdDev(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2)
        {
            return 0m;
        }

        var mean = values.Average();
        var sumSquares = values.Sum(v => (v - mean) * (v - mean));
        var variance = sumSquares / (values.Count - 1);
        return (decimal)Math.Sqrt((double)variance);
    }
}
