namespace Coffer.Core.Budgeting;

/// <summary>
/// A budgeted category's current-month tracking: the real (user-defined) category name/colour and its
/// <see cref="BudgetStatus"/>.
/// </summary>
public sealed record BudgetLine(
    Guid CategoryId,
    string CategoryName,
    string? CategoryColor,
    BudgetStatus Status);

/// <summary>
/// A category with spend this month but no active budget. <see cref="CategoryId"/>/<see cref="CategoryName"/>
/// are <c>null</c> for the uncategorised bucket (localised by the presentation layer), so category-less
/// spend is shown rather than hidden.
/// </summary>
public sealed record UnbudgetedLine(
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryColor,
    decimal Spent);

/// <summary>
/// Everything the Budgets page renders for one month: the anchored <see cref="Month"/> (its first day),
/// the tracked <see cref="Budgets"/>, and the <see cref="Unbudgeted"/> lines.
/// </summary>
public sealed record BudgetOverview(
    DateOnly Month,
    IReadOnlyList<BudgetLine> Budgets,
    IReadOnlyList<UnbudgetedLine> Unbudgeted);
