namespace Coffer.Core.Budgeting;

/// <summary>How a category's month-to-date spend sits against its limit.</summary>
public enum BudgetZone
{
    /// <summary>Comfortably within the limit and on pace.</summary>
    Ok,

    /// <summary>At/over 80% spent, or the linear projection would exceed the limit.</summary>
    Warning,

    /// <summary>Already at or over the limit.</summary>
    Over,
}

/// <summary>
/// A budget's mid-month state, produced by <see cref="BudgetTrackingEngine"/>. All amounts are
/// <c>decimal</c> (hard rule #1). <see cref="Remaining"/> may go negative once over. <see cref="Fraction"/>
/// is <c>Spent / Limit</c> (0..&gt;1). <see cref="Projected"/> is the linear end-of-month estimate.
/// </summary>
public sealed record BudgetStatus(
    decimal Limit,
    decimal Spent,
    decimal Remaining,
    decimal Fraction,
    decimal Projected,
    BudgetZone Zone);
