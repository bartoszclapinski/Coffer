using Coffer.Core.Domain;
using Coffer.Core.Planning;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Planning;

/// <summary>EF-backed persistence for <see cref="RecurringFlow"/> over the encrypted SQLite store.</summary>
public sealed class RecurringFlowRepository : IRecurringFlowRepository
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public RecurringFlowRepository(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<RecurringFlow>> GetAllAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.RecurringFlows.AsNoTracking()
            .OrderBy(f => f.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RecurringFlow>> GetActiveAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.RecurringFlows.AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.AnchorDayOfMonth)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(RecurringFlow flow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(flow);
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.RecurringFlows.Add(flow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(RecurringFlow flow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(flow);
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.RecurringFlows.Update(flow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await db.RecurringFlows.Where(f => f.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }
}
