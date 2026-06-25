using Coffer.Core.Domain;

namespace Coffer.Core.Goals;

/// <summary>
/// Write-side lifecycle for goals on the Doradca page: create, edit the scalar fields, archive
/// (a soft delete — the engine and query skip archived goals), and add or remove manual
/// contributions. Money is <c>decimal</c> (hard rule #1); contribution dates are
/// transaction-scale <see cref="DateOnly"/> (hard rule #2). Nothing here calls the AI — the
/// engine calculates, the 14-C commentator only explains.
/// </summary>
public interface IGoalService
{
    Task<Guid> CreateAsync(NewGoal goal, CancellationToken ct);

    Task UpdateAsync(Goal goal, CancellationToken ct);

    Task ArchiveAsync(Guid goalId, CancellationToken ct);

    Task AddContributionAsync(Guid goalId, decimal amount, DateOnly date, CancellationToken ct);

    Task RemoveContributionAsync(Guid contributionId, CancellationToken ct);
}
