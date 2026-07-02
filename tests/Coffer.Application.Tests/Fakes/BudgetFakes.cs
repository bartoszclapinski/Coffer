using Coffer.Core.Budgeting;

namespace Coffer.Application.Tests.Fakes;

/// <summary>
/// In-memory <see cref="ICategoryBudgetRepository"/>: serves a seeded active list and records the
/// upsert/remove calls so the budgets view model's writes can be asserted.
/// </summary>
internal sealed class FakeCategoryBudgetRepository : ICategoryBudgetRepository
{
    public List<CategoryBudgetItem> Active { get; } = [];

    public int SetCalls { get; private set; }

    public (Guid CategoryId, decimal Limit, string Currency)? LastSet { get; private set; }

    public int RemoveCalls { get; private set; }

    public Guid? LastRemoved { get; private set; }

    public Task<IReadOnlyList<CategoryBudgetItem>> GetActiveAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CategoryBudgetItem>>([.. Active]);

    public Task SetBudgetAsync(Guid categoryId, decimal limitAmount, string currency, CancellationToken ct)
    {
        SetCalls++;
        LastSet = (categoryId, limitAmount, currency);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid categoryId, CancellationToken ct)
    {
        RemoveCalls++;
        LastRemoved = categoryId;
        return Task.CompletedTask;
    }
}

/// <summary>In-memory <see cref="IBudgetTrackingQuery"/> returning a seeded overview, recording each call.</summary>
internal sealed class FakeBudgetTrackingQuery : IBudgetTrackingQuery
{
    public BudgetOverview Overview { get; set; } =
        new(new DateOnly(2026, 3, 1), [], []);

    public int Calls { get; private set; }

    public Task<BudgetOverview> GetOverviewAsync(Guid? accountId, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Overview);
    }
}
