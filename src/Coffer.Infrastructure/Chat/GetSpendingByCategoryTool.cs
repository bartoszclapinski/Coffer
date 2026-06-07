using System.Text.Json;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Chat;

/// <summary>
/// Spend broken down by category over a date range. Server-side <c>GROUP BY CategoryId</c> on
/// debits, mapped to category names (null → "Bez kategorii"), returned as positive magnitudes in
/// PLN, sorted by largest spend first.
/// </summary>
public sealed class GetSpendingByCategoryTool : ChatTool
{
    public GetSpendingByCategoryTool(IDbContextFactory<CofferDbContext> contextFactory)
        : base(contextFactory)
    {
    }

    public override string Name => "GetSpendingByCategory";

    public override string Description =>
        "Wydatki w rozbiciu na kategorie w zadanym przedziale dat. Zwraca listę kategorii z sumami "
        + "(dodatnie kwoty w PLN), od największej. Daty w formacie RRRR-MM-DD.";

    public override string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "from": { "type": "string", "description": "Data początkowa włącznie, format RRRR-MM-DD." },
            "to": { "type": "string", "description": "Data końcowa włącznie, format RRRR-MM-DD." }
          },
          "required": ["from", "to"]
        }
        """;

    private protected override async Task<object> RunAsync(JsonElement args, CofferDbContext db, CancellationToken ct)
    {
        if (!TryGetDate(args, "from", out var from) || !TryGetDate(args, "to", out var to))
        {
            return ErrorObject("Both 'from' and 'to' are required dates in RRRR-MM-DD format.");
        }

        if (from > to)
        {
            return ErrorObject("'from' must be on or before 'to'.");
        }

        var totals = await db.Transactions.AsNoTracking()
            .Where(t => t.Currency == _displayCurrency && t.Date >= from && t.Date <= to && t.Amount < 0)
            .GroupBy(t => t.CategoryId)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var ids = totals.Where(t => t.CategoryId != null).Select(t => t.CategoryId!.Value).ToList();
        var names = await db.Categories.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct)
            .ConfigureAwait(false);

        var categories = totals
            .Select(t => new
            {
                category = t.CategoryId is { } id && names.TryGetValue(id, out var name) ? name : _uncategorizedLabel,
                total = -t.Total,
            })
            .OrderByDescending(c => c.total)
            .ToList();

        return new { from = Iso(from), to = Iso(to), currency = _displayCurrency, categories };
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");
}
