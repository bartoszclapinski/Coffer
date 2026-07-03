namespace Coffer.Core.Forecasting;

/// <summary>
/// The assembled inputs for one category, handed to <see cref="ExpenseForecastEngine"/>. <see cref="Fixed"/>
/// is the sum of recurring outflows attributed to the category that land in the target month;
/// <see cref="Variable"/> is the trailing-history monthly estimate of its non-recurring spend;
/// <see cref="CurrentLimit"/> is the category's active budget limit, if any. <see cref="CategoryId"/>/
/// <see cref="CategoryName"/> are <c>null</c> for the combined uncategorised/unattributed bucket.
/// </summary>
public sealed record CategoryForecastInput(
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryColor,
    decimal Fixed,
    decimal Variable,
    decimal? CurrentLimit);

/// <summary>
/// A category's next-month forecast: the <see cref="Fixed"/> (recurring) and <see cref="Variable"/>
/// (discretionary) parts, their <see cref="Total"/>, and a <see cref="SuggestedLimit"/> (the total with
/// headroom) shown against the category's <see cref="CurrentLimit"/> so the owner can accept it as a
/// budget. All amounts <c>decimal</c> (rule #1), positive magnitudes.
/// </summary>
public sealed record CategoryForecast(
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryColor,
    decimal Fixed,
    decimal Variable,
    decimal Total,
    decimal SuggestedLimit,
    decimal? CurrentLimit);

/// <summary>
/// The whole next-month forecast: the target <see cref="Month"/> (its first day), the per-category
/// <see cref="Categories"/> (largest total first), and the grand <see cref="Total"/>.
/// </summary>
public sealed record ExpenseForecast(
    DateOnly Month,
    IReadOnlyList<CategoryForecast> Categories,
    decimal Total);
