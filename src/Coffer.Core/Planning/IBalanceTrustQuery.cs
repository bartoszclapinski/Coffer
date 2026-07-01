namespace Coffer.Core.Planning;

/// <summary>
/// Answers "is the balance for this account trustworthy as of a date?" — i.e. is the statement history
/// contiguous across the window the derived balance depends on. The window starts at the account's
/// anchor date when one is set, otherwise at its earliest imported transaction, and ends at the as-of
/// date. When a <see cref="StatementGap"/> falls inside that window the derived balance is unreliable,
/// so an affordability answer built on it must be flagged uncertain rather than shown as a confident
/// number.
/// </summary>
public interface IBalanceTrustQuery
{
    Task<BalanceTrust> CheckAsync(Guid accountId, DateOnly asOf, CancellationToken ct);
}
