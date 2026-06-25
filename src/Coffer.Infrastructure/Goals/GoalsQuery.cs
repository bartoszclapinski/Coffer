using Coffer.Core.Domain;
using Coffer.Core.Goals;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Goals;

/// <summary>
/// Read-side query for the active goals shown on the Doradca page. Loads each goal's
/// <see cref="Goal.Contributions"/> so the <see cref="IGoalFeasibilityEngine"/> can score it,
/// runs untracked, and orders most-urgent first (highest <see cref="Goal.Priority"/>, then the
/// earliest <see cref="Goal.TargetDate"/>). Archived goals are excluded.
/// </summary>
public sealed class GoalsQuery : IGoalsQuery
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public GoalsQuery(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<Goal>> GetActiveAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var goals = await db.Goals.AsNoTracking()
            .Include(g => g.Contributions)
            .Where(g => !g.IsArchived)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Priority is persisted as a string, so ordering it server-side would sort alphabetically
        // (Medium > Low > High). Order in memory where the enum compares by its int value.
        return goals
            .OrderByDescending(g => g.Priority)
            .ThenBy(g => g.TargetDate)
            .ToList();
    }
}
