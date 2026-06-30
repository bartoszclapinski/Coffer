using Coffer.Core.Domain;

namespace Coffer.Core.Planning;

/// <summary>Persistence for the owner's recurring flows — the planning page's CRUD surface.</summary>
public interface IRecurringFlowRepository
{
    Task<IReadOnlyList<RecurringFlow>> GetAllAsync(CancellationToken ct);

    Task<IReadOnlyList<RecurringFlow>> GetActiveAsync(CancellationToken ct);

    Task AddAsync(RecurringFlow flow, CancellationToken ct);

    Task UpdateAsync(RecurringFlow flow, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}
