using Coffer.Core.Domain;
using Coffer.Core.Goals;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Goals;

/// <summary>
/// Mutates goal lifecycle state for the Doradca page. Editing copies only the scalar fields onto
/// the tracked entity, so a goal's contributions are never disturbed by an edit. Archiving is a
/// soft delete — the row stays for history but <see cref="GoalsQuery"/> and the engine skip it.
/// <see cref="CreateAsync"/> stamps <see cref="Goal.CreatedAt"/> in UTC (hard rule #2).
/// </summary>
public sealed class GoalService : IGoalService
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public GoalService(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<Guid> CreateAsync(NewGoal goal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(goal);
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var entity = new Goal
        {
            Id = Guid.NewGuid(),
            Name = goal.Name,
            Type = goal.Type,
            TargetAmount = goal.TargetAmount,
            Currency = goal.Currency,
            TargetDate = goal.TargetDate,
            Priority = goal.Priority,
            Notes = goal.Notes,
            IsArchived = false,
            CreatedAt = DateTime.UtcNow,
        };

        db.Goals.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task UpdateAsync(Goal goal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(goal);
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var entity = await db.Goals.FirstOrDefaultAsync(g => g.Id == goal.Id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        entity.Name = goal.Name;
        entity.Type = goal.Type;
        entity.TargetAmount = goal.TargetAmount;
        entity.Currency = goal.Currency;
        entity.TargetDate = goal.TargetDate;
        entity.Priority = goal.Priority;
        entity.Notes = goal.Notes;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ArchiveAsync(Guid goalId, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var entity = await db.Goals.FirstOrDefaultAsync(g => g.Id == goalId, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        entity.IsArchived = true;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task AddContributionAsync(Guid goalId, decimal amount, DateOnly date, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var exists = await db.Goals.AnyAsync(g => g.Id == goalId, ct).ConfigureAwait(false);
        if (!exists)
        {
            return;
        }

        db.GoalContributions.Add(new GoalContribution
        {
            Id = Guid.NewGuid(),
            GoalId = goalId,
            Amount = amount,
            Date = date,
            Source = ContributionSource.Manual,
            TransactionId = null,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveContributionAsync(Guid contributionId, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var entity = await db.GoalContributions
            .FirstOrDefaultAsync(c => c.Id == contributionId, ct)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        db.GoalContributions.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
