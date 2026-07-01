using Coffer.Core.Domain;
using Coffer.Core.Planning;

namespace Coffer.Application.Tests.Fakes;

/// <summary>In-memory <see cref="IRecurringFlowRepository"/> backing the planning VM tests.</summary>
internal sealed class FakeRecurringFlowRepository : IRecurringFlowRepository
{
    public FakeRecurringFlowRepository(params RecurringFlow[] flows) => Store.AddRange(flows);

    public List<RecurringFlow> Store { get; } = [];

    public Task<IReadOnlyList<RecurringFlow>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RecurringFlow>>(Store.ToList());

    public Task<IReadOnlyList<RecurringFlow>> GetActiveAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RecurringFlow>>(Store.Where(f => f.IsActive).ToList());

    public Task AddAsync(RecurringFlow flow, CancellationToken ct)
    {
        Store.Add(flow);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(RecurringFlow flow, CancellationToken ct)
    {
        var index = Store.FindIndex(f => f.Id == flow.Id);
        if (index >= 0)
        {
            Store[index] = flow;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct)
    {
        Store.RemoveAll(f => f.Id == id);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory <see cref="IRecurringFlowDetector"/> that counts how often it is queried.</summary>
internal sealed class FakeRecurringFlowDetector : IRecurringFlowDetector
{
    public List<RecurringFlowCandidate> Candidates { get; } = [];

    public int Calls { get; private set; }

    public Task<IReadOnlyList<RecurringFlowCandidate>> DetectAsync(CancellationToken ct)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<RecurringFlowCandidate>>(Candidates.ToList());
    }
}

/// <summary>In-memory <see cref="IRunningBalanceQuery"/> returning a fixed opening balance.</summary>
internal sealed class FakeRunningBalanceQuery : IRunningBalanceQuery
{
    public decimal Balance { get; set; }

    public Task<decimal> GetBalanceAsOfAsync(DateOnly asOf, Guid? accountId, CancellationToken ct) =>
        Task.FromResult(Balance);
}

/// <summary>In-memory <see cref="IStatementContinuityChecker"/> returning seeded gaps.</summary>
internal sealed class FakeStatementContinuityChecker : IStatementContinuityChecker
{
    public List<StatementGap> Gaps { get; } = [];

    public Task<IReadOnlyList<StatementGap>> FindGapsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StatementGap>>(Gaps.ToList());
}

/// <summary>
/// In-memory <see cref="ICashFlowExplainer"/> returning a scripted explanation and recording the
/// projection it was handed, so VM tests can assert the narrative is surfaced without any AI.
/// </summary>
internal sealed class FakeCashFlowExplainer : ICashFlowExplainer
{
    public CashFlowExplanation Result { get; set; } = new("explanation", GeneratedByAi: true);

    public CashFlowProjection? LastProjection { get; private set; }

    public Task<CashFlowExplanation> ExplainAsync(CashFlowProjection projection, CancellationToken ct)
    {
        LastProjection = projection;
        return Task.FromResult(Result);
    }
}
