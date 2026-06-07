namespace Coffer.Core.Dashboard;

/// <summary>
/// A single point on a spend trend: the positive spend <see cref="Total"/> for the
/// bucket starting at <see cref="Date"/> (a calendar day for the daily trend, the
/// first of the month for the monthly trend). Buckets with no spend are present with
/// <see cref="Total"/> = 0 so the series has no gaps.
/// </summary>
public sealed record TrendPoint(DateOnly Date, decimal Total);
