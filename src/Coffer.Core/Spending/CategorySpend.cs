namespace Coffer.Core.Spending;

/// <summary>
/// One category's spend within a window: the positive magnitude of its debits (<see cref="Total"/>,
/// always <c>decimal</c>, hard rule #1), its <see cref="Share"/> of the window's total spend (0..1),
/// and the number of contributing transactions. <see cref="CategoryName"/>/<see cref="CategoryColor"/>
/// carry the real (user-defined, Polish) category data, or are <c>null</c> for the uncategorised bucket —
/// the presentation layer localises that fallback label, keeping <c>Coffer.Core</c> presentation-free.
/// </summary>
public sealed record CategorySpend(
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryColor,
    decimal Total,
    decimal Share,
    int Count);
