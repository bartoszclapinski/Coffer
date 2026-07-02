using Coffer.Core.Domain;
using Coffer.Core.Spending;
using Coffer.Core.Transactions;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Spending;

/// <summary>
/// Read-side spending-explorer aggregation over <c>Transactions</c>. Every level is computed server-side
/// (<c>GROUP BY</c> / <c>SUM</c>, <c>AsNoTracking</c>) and scoped to the single display currency (PLN;
/// multi-currency is a later phase), to debits only (<c>Amount &lt; 0</c>), and to the inclusive window —
/// optionally narrowed to one account. Spend is returned as the positive magnitude of those debits, so the
/// UI never juggles signs. Category names/colours are the real user data; the uncategorised and
/// unknown-merchant buckets come back with <c>null</c> identifiers for the presentation layer to localise.
/// Conventions mirror <see cref="Chat.GetSpendingByCategoryTool"/> and <see cref="Dashboard.DashboardQuery"/>.
/// </summary>
public sealed class SpendingExplorerQuery : ISpendingExplorerQuery
{
    private const string DisplayCurrency = "PLN";

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public SpendingExplorerQuery(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<CategorySpend>> GetCategoriesAsync(
        SpendingWindow window, Guid? accountId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(window);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var totals = await Debits(db, window, accountId)
            .GroupBy(t => t.CategoryId)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount), Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (totals.Count == 0)
        {
            return [];
        }

        var ids = totals.Where(t => t.CategoryId != null).Select(t => t.CategoryId!.Value).ToList();
        var meta = await db.Categories.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Color })
            .ToDictionaryAsync(c => c.Id, c => (c.Name, c.Color), ct)
            .ConfigureAwait(false);

        var grandTotal = totals.Sum(t => -t.Total);
        if (grandTotal <= 0m)
        {
            return [];
        }

        return totals
            .Select(t =>
            {
                var magnitude = -t.Total;
                var (name, color) = t.CategoryId is { } id && meta.TryGetValue(id, out var m)
                    ? (m.Name, (string?)m.Color)
                    : (null, null);
                return new CategorySpend(t.CategoryId, name, color, magnitude, magnitude / grandTotal, t.Count);
            })
            .OrderByDescending(c => c.Total)
            .ToList();
    }

    public async Task<IReadOnlyList<MerchantSpend>> GetMerchantsAsync(
        SpendingWindow window, Guid? categoryId, Guid? accountId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(window);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var raw = await ApplyCategory(Debits(db, window, accountId), categoryId)
            .GroupBy(t => t.Merchant)
            .Select(g => new { g.Key, Total = g.Sum(t => t.Amount), Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (raw.Count == 0)
        {
            return [];
        }

        // Collapse null and blank merchants into a single "unknown" bucket. A Dictionary cannot key on
        // null, so the unknown bucket is tracked separately and appended with a null Merchant.
        var known = new Dictionary<string, (decimal Total, int Count)>(StringComparer.Ordinal);
        var unknownTotal = 0m;
        var unknownCount = 0;
        foreach (var r in raw)
        {
            if (string.IsNullOrWhiteSpace(r.Key))
            {
                unknownTotal += r.Total;
                unknownCount += r.Count;
            }
            else
            {
                known[r.Key] = (r.Total, r.Count);
            }
        }

        var grandTotal = -(known.Values.Sum(v => v.Total) + unknownTotal);
        if (grandTotal <= 0m)
        {
            return [];
        }

        var merchants = known
            .Select(kv => new MerchantSpend(kv.Key, -kv.Value.Total, -kv.Value.Total / grandTotal, kv.Value.Count))
            .ToList();
        if (unknownCount > 0)
        {
            merchants.Add(new MerchantSpend(null, -unknownTotal, -unknownTotal / grandTotal, unknownCount));
        }

        return merchants.OrderByDescending(m => m.Total).ToList();
    }

    public async Task<IReadOnlyList<TransactionListItem>> GetTransactionsAsync(
        SpendingWindow window, Guid? categoryId, string? merchant, Guid? accountId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(window);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var scope = ApplyCategory(Debits(db, window, accountId), categoryId);
        scope = merchant is null
            ? scope.Where(t => t.Merchant == null || t.Merchant.Trim() == "")
            : scope.Where(t => t.Merchant == merchant);

        return await scope
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
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

    private static IQueryable<Transaction> Debits(CofferDbContext db, SpendingWindow window, Guid? accountId)
    {
        var scope = db.Transactions.AsNoTracking()
            .Where(t => t.Currency == DisplayCurrency
                && t.Amount < 0
                && t.Date >= window.From
                && t.Date <= window.To);
        return accountId is { } id ? scope.Where(t => t.AccountId == id) : scope;
    }

    // A drill-down always starts from a concrete category slice, so null means the uncategorised
    // bucket (CategoryId == null), never "all categories".
    private static IQueryable<Transaction> ApplyCategory(IQueryable<Transaction> scope, Guid? categoryId) =>
        categoryId is { } id ? scope.Where(t => t.CategoryId == id) : scope.Where(t => t.CategoryId == null);
}
