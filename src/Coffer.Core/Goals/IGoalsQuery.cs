using Coffer.Core.Domain;

namespace Coffer.Core.Goals;

/// <summary>
/// Read-side query for the Doradca page: the active (non-archived) goals with their
/// <see cref="Goal.Contributions"/> loaded, so the <see cref="IGoalFeasibilityEngine"/> can evaluate
/// each one. Ordered most-urgent first (highest priority, then earliest target date).
/// </summary>
public interface IGoalsQuery
{
    Task<IReadOnlyList<Goal>> GetActiveAsync(CancellationToken ct);
}
