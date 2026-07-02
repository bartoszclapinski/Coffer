namespace Coffer.Core.Budgeting;

/// <summary>
/// An active category budget with its category's display data, for the Budgets editor and the category
/// picker's current values.
/// </summary>
public sealed record CategoryBudgetItem(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string CategoryColor,
    decimal LimitAmount,
    string Currency);

/// <summary>
/// CRUD for <see cref="Domain.CategoryBudget"/> limits. The implementation lives in
/// <c>Coffer.Infrastructure</c>; the <c>Coffer.Application</c> budgets view model depends on this
/// abstraction. At most one active budget exists per (category, currency): <see cref="SetBudgetAsync"/>
/// upserts it.
/// </summary>
public interface ICategoryBudgetRepository
{
    /// <summary>Active budgets joined to their category, ordered by category name.</summary>
    Task<IReadOnlyList<CategoryBudgetItem>> GetActiveAsync(CancellationToken ct);

    /// <summary>
    /// Sets the monthly limit for a category: updates the existing active budget for that
    /// (category, currency) or creates one. Throws if the category does not exist.
    /// </summary>
    Task SetBudgetAsync(Guid categoryId, decimal limitAmount, string currency, CancellationToken ct);

    /// <summary>Removes the active budget(s) for a category. A no-op if none exists.</summary>
    Task RemoveAsync(Guid categoryId, CancellationToken ct);
}
