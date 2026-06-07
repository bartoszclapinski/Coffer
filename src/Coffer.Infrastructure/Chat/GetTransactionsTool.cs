using System.Text.Json;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Chat;

/// <summary>
/// Returns individual transactions in a date range, optionally filtered by a merchant/description
/// substring and/or category, newest first and capped at <see cref="_maxLimit"/>. Read-only
/// projection (no entities materialised); amounts are signed (negative = wydatek).
/// </summary>
public sealed class GetTransactionsTool : ChatTool
{
    private const int _defaultLimit = 20;
    private const int _maxLimit = 50;

    public GetTransactionsTool(IDbContextFactory<CofferDbContext> contextFactory)
        : base(contextFactory)
    {
    }

    public override string Name => "GetTransactions";

    public override string Description =>
        "Lista pojedynczych transakcji w przedziale dat, opcjonalnie filtrowana po fragmencie nazwy "
        + "sprzedawcy/opisu oraz po kategorii. Od najnowszych, maksymalnie 50. Kwoty są ze znakiem "
        + "(ujemne = wydatek). Daty w formacie RRRR-MM-DD.";

    public override string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "from": { "type": "string", "description": "Data początkowa włącznie, format RRRR-MM-DD." },
            "to": { "type": "string", "description": "Data końcowa włącznie, format RRRR-MM-DD." },
            "merchantPattern": { "type": "string", "description": "Opcjonalny fragment nazwy sprzedawcy lub opisu." },
            "category": { "type": "string", "description": "Opcjonalna nazwa kategorii (po polsku)." },
            "limit": { "type": "integer", "description": "Maksymalna liczba wyników (1-50, domyślnie 20)." }
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

        var limit = Math.Clamp(GetInt(args, "limit", _defaultLimit), 1, _maxLimit);
        var category = await ResolveCategoryAsync(db, GetString(args, "category"), ct).ConfigureAwait(false);
        if (category.Kind == CategoryMatchKind.Unknown)
        {
            return new { from = Iso(from), to = Iso(to), currency = _displayCurrency, count = 0, transactions = Array.Empty<object>() };
        }

        var query = db.Transactions.AsNoTracking()
            .Where(t => t.Currency == _displayCurrency && t.Date >= from && t.Date <= to);
        query = ApplyCategory(query, category);

        var pattern = GetString(args, "merchantPattern");
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            var needle = pattern.Trim();
            query = query.Where(t => t.Description.Contains(needle) || (t.Merchant != null && t.Merchant.Contains(needle)));
        }

        var rows = await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .Take(limit)
            .Select(t => new
            {
                date = t.Date,
                description = t.Description,
                merchant = t.Merchant,
                amount = t.Amount,
                currency = t.Currency,
                category = t.Category != null ? t.Category.Name : _uncategorizedLabel,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var transactions = rows
            .Select(r => new
            {
                date = r.date.ToString("yyyy-MM-dd"),
                description = r.description,
                merchant = r.merchant,
                amount = r.amount,
                currency = r.currency,
                category = r.category,
            })
            .ToList();

        return new { from = Iso(from), to = Iso(to), currency = _displayCurrency, count = transactions.Count, transactions };
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");
}
