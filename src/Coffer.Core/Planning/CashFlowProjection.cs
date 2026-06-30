namespace Coffer.Core.Planning;

/// <summary>
/// The deterministic forward cash-flow projection produced by <see cref="CashFlowProjectionEngine"/>:
/// the window, the opening/closing balance, the lowest point the running balance reaches (and when),
/// and the dated event timeline. All money is <c>decimal</c> (rule #1); all dates are
/// <see cref="DateOnly"/> (rule #2).
/// </summary>
public sealed record CashFlowProjection(
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal LowestBalance,
    DateOnly? LowestBalanceDate,
    IReadOnlyList<CashFlowEvent> Events)
{
    /// <summary>True when the running balance crossed the tight floor at any point in the window.</summary>
    public bool HasTightWindow => Events.Any(e => e.IsTight);
}
