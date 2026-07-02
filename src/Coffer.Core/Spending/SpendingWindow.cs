namespace Coffer.Core.Spending;

/// <summary>
/// An inclusive date range for the spending explorer: both <see cref="From"/> and <see cref="To"/>
/// are included in every aggregation. Dates are <see cref="DateOnly"/> (hard rule #2, operation dates).
/// </summary>
public sealed record SpendingWindow(DateOnly From, DateOnly To);
