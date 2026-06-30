namespace Coffer.Core.Planning;

/// <summary>
/// Computes the account balance as of a date from the running sum of imported transactions — the
/// opening balance the <see cref="CashFlowProjectionEngine"/> projects forward from. Trustworthy only
/// while the imported history is contiguous (see <see cref="IStatementContinuityChecker"/>).
/// </summary>
public interface IRunningBalanceQuery
{
    Task<decimal> GetBalanceAsOfAsync(DateOnly asOf, CancellationToken ct);
}
