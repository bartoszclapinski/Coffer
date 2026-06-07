using System.Text.Json;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Chat;

/// <summary>
/// Total amount spent (magnitude of debits) over a date range, optionally for one category.
/// Server-side <c>SUM</c> of negative amounts, returned as a positive figure in PLN.
/// </summary>
public sealed class GetTotalSpentTool : ChatTool
{
    public GetTotalSpentTool(IDbContextFactory<CofferDbContext> contextFactory)
        : base(contextFactory)
    {
    }

    public override string Name => "GetTotalSpent";

    public override string Description =>
        "Suma wydatków (wartość bezwzględna obciążeń) w zadanym przedziale dat, opcjonalnie dla jednej "
        + "kategorii. Zwraca kwotę dodatnią w PLN. Daty w formacie RRRR-MM-DD.";

    public override string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "from": { "type": "string", "description": "Data początkowa włącznie, format RRRR-MM-DD." },
            "to": { "type": "string", "description": "Data końcowa włącznie, format RRRR-MM-DD." },
            "category": { "type": "string", "description": "Opcjonalna nazwa kategorii (po polsku)." }
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

        var category = await ResolveCategoryAsync(db, GetString(args, "category"), ct).ConfigureAwait(false);
        if (category.Kind == CategoryMatchKind.Unknown)
        {
            return new { from = Iso(from), to = Iso(to), category = GetString(args, "category"), currency = _displayCurrency, totalSpent = 0m };
        }

        var query = db.Transactions.AsNoTracking()
            .Where(t => t.Currency == _displayCurrency && t.Date >= from && t.Date <= to && t.Amount < 0);
        query = ApplyCategory(query, category);

        var sum = await query.SumAsync(t => (decimal?)t.Amount, ct).ConfigureAwait(false) ?? 0m;

        return new
        {
            from = Iso(from),
            to = Iso(to),
            category = GetString(args, "category"),
            currency = _displayCurrency,
            totalSpent = -sum,
        };
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");
}
