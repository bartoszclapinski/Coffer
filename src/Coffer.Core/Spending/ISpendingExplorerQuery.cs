using Coffer.Core.Transactions;

namespace Coffer.Core.Spending;

/// <summary>
/// Read-side query for the interactive spending explorer. The implementation lives in
/// <c>Coffer.Infrastructure</c> and aggregates over the EF context server-side (<c>GROUP BY</c>,
/// <c>AsNoTracking</c>); <c>Coffer.Application</c> view models depend on this abstraction. Every level
/// considers only debits (spend) in the single display currency, returned as positive magnitudes,
/// ordered largest first, optionally scoped to one account.
///
/// The three levels form a drill-down: window → <see cref="GetCategoriesAsync"/> → pick a category →
/// <see cref="GetMerchantsAsync"/> → pick a merchant → <see cref="GetTransactionsAsync"/>. Because a
/// drill always starts from a concrete selection, a <c>null</c> <c>categoryId</c> means the
/// <em>uncategorised</em> bucket (not "all categories") and a <c>null</c> <c>merchant</c> means the
/// <em>unknown-merchant</em> bucket (not "all merchants").
/// </summary>
public interface ISpendingExplorerQuery
{
    /// <summary>Spend by category within <paramref name="window"/>, optionally scoped to one account.</summary>
    Task<IReadOnlyList<CategorySpend>> GetCategoriesAsync(
        SpendingWindow window, Guid? accountId, CancellationToken ct);

    /// <summary>
    /// Spend by merchant within <paramref name="window"/> and the selected category
    /// (<paramref name="categoryId"/> <c>null</c> = the uncategorised bucket), optionally scoped to one
    /// account. Null/blank merchants collapse into one "unknown merchant" bucket (<c>Merchant == null</c>).
    /// </summary>
    Task<IReadOnlyList<MerchantSpend>> GetMerchantsAsync(
        SpendingWindow window, Guid? categoryId, Guid? accountId, CancellationToken ct);

    /// <summary>
    /// The transactions behind one merchant (<paramref name="merchant"/> <c>null</c> = the unknown-merchant
    /// bucket) in the selected category (<paramref name="categoryId"/> <c>null</c> = uncategorised) within
    /// <paramref name="window"/>, optionally scoped to one account, newest first.
    /// </summary>
    Task<IReadOnlyList<TransactionListItem>> GetTransactionsAsync(
        SpendingWindow window, Guid? categoryId, string? merchant, Guid? accountId, CancellationToken ct);
}
