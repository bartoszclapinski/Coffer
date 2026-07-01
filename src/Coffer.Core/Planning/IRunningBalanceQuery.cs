namespace Coffer.Core.Planning;

/// <summary>
/// Computes the account balance as of a date — the opening balance the
/// <see cref="CashFlowProjectionEngine"/> projects forward from. Trustworthy only while the imported
/// history is contiguous (see <see cref="IStatementContinuityChecker"/>).
/// </summary>
public interface IRunningBalanceQuery
{
    /// <summary>
    /// The balance as of <paramref name="asOf"/> for one account, or the cross-account running sum when
    /// <paramref name="accountId"/> is null. When the account has an anchor
    /// (<see cref="Domain.Account.AnchorDate"/>/<see cref="Domain.Account.AnchorBalance"/>) the result is
    /// absolute: the anchor balance plus the sum of the account's transactions after the anchor date up to
    /// <paramref name="asOf"/>. Without an anchor it is the relative running sum of transactions up to
    /// <paramref name="asOf"/>, scoped to the account when one is given.
    /// </summary>
    Task<decimal> GetBalanceAsOfAsync(DateOnly asOf, Guid? accountId, CancellationToken ct);
}
