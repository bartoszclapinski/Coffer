using System.Text.Json;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Chat;

/// <summary>
/// Monthly spend for one category over the last N calendar months (ending on the current month).
/// Server-side <c>GROUP BY</c> year-month on debits; gaps are filled with zero so the model sees a
/// contiguous series. Positive magnitudes in PLN.
/// </summary>
public sealed class GetMonthlyTrendTool : ChatTool
{
    private const int _defaultMonths = 12;
    private const int _maxMonths = 24;

    public GetMonthlyTrendTool(IDbContextFactory<CofferDbContext> contextFactory)
        : base(contextFactory)
    {
    }

    public override string Name => "GetMonthlyTrend";

    public override string Description =>
        "Miesięczne wydatki dla jednej kategorii w ostatnich N miesiącach (do bieżącego miesiąca "
        + "włącznie). Zwraca ciągłą serię (brakujące miesiące = 0), dodatnie kwoty w PLN.";

    public override string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "category": { "type": "string", "description": "Nazwa kategorii (po polsku)." },
            "months": { "type": "integer", "description": "Liczba miesięcy wstecz (1-24, domyślnie 12)." }
          },
          "required": ["category"]
        }
        """;

    private protected override async Task<object> RunAsync(JsonElement args, CofferDbContext db, CancellationToken ct)
    {
        var categoryName = GetString(args, "category");
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return ErrorObject("'category' is required.");
        }

        var months = Math.Clamp(GetInt(args, "months", _defaultMonths), 1, _maxMonths);
        var category = await ResolveCategoryAsync(db, categoryName, ct).ConfigureAwait(false);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var windowStart = monthStart.AddMonths(-(months - 1));
        var windowEnd = monthStart.AddMonths(1);

        var lookup = new Dictionary<(int Year, int Month), decimal>();
        if (category.Kind != CategoryMatchKind.Unknown)
        {
            var query = db.Transactions.AsNoTracking()
                .Where(t => t.Currency == _displayCurrency && t.Amount < 0 && t.Date >= windowStart && t.Date < windowEnd);
            query = ApplyCategory(query, category);

            var byMonth = await query
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(t => t.Amount) })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            lookup = byMonth.ToDictionary(m => (m.Year, m.Month), m => -m.Total);
        }

        var series = new List<object>(months);
        for (var i = 0; i < months; i++)
        {
            var bucket = windowStart.AddMonths(i);
            var total = lookup.TryGetValue((bucket.Year, bucket.Month), out var value) ? value : 0m;
            series.Add(new { month = bucket.ToString("yyyy-MM"), total });
        }

        return new { category = categoryName, currency = _displayCurrency, months = series };
    }
}
