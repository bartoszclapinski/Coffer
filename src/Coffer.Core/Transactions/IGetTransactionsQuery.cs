namespace Coffer.Core.Transactions;

/// <summary>
/// Read-side query for the transactions list and its account filter. The
/// implementation lives in <c>Coffer.Infrastructure</c> (it queries the EF
/// context); <c>Coffer.Application</c> view models depend on this abstraction.
/// </summary>
public interface IGetTransactionsQuery
{
    /// <summary>
    /// Returns transactions matching <paramref name="filter"/>, newest first. With
    /// no date set, defaults to the last six months.
    /// </summary>
    Task<IReadOnlyList<TransactionListItem>> ExecuteAsync(TransactionQueryFilter filter, CancellationToken ct);

    /// <summary>
    /// Returns the non-archived accounts for the filter dropdown, ordered by name.
    /// </summary>
    Task<IReadOnlyList<AccountListItem>> GetAccountsAsync(CancellationToken ct);
}
