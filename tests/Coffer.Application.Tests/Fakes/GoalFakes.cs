using Coffer.Core.Domain;
using Coffer.Core.Goals;

namespace Coffer.Application.Tests.Fakes;

public sealed class FakeGoalsQuery : IGoalsQuery
{
    public List<Goal> Goals { get; } = [];

    public int Calls { get; private set; }

    public Task<IReadOnlyList<Goal>> GetActiveAsync(CancellationToken ct)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<Goal>>(Goals.Where(g => !g.IsArchived).ToList());
    }
}

public sealed class FakeAdvisorReportQuery : IAdvisorReportQuery
{
    public AdvisorReport? Report { get; set; }

    public int Calls { get; private set; }

    public Task<AdvisorReport?> GetLatestAsync(CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Report);
    }
}

public sealed class FakeGoalService : IGoalService
{
    private readonly List<Goal> _store;

    public FakeGoalService(List<Goal> store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public List<NewGoal> Created { get; } = [];

    public List<Guid> Archived { get; } = [];

    public List<(Guid GoalId, decimal Amount, DateOnly Date)> Contributions { get; } = [];

    public Task<Guid> CreateAsync(NewGoal goal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(goal);
        Created.Add(goal);
        var id = Guid.NewGuid();
        _store.Add(new Goal
        {
            Id = id,
            Name = goal.Name,
            Type = goal.Type,
            TargetAmount = goal.TargetAmount,
            Currency = goal.Currency,
            TargetDate = goal.TargetDate,
            Priority = goal.Priority,
            Notes = goal.Notes,
            CreatedAt = DateTime.UtcNow,
        });
        return Task.FromResult(id);
    }

    public Task UpdateAsync(Goal goal, CancellationToken ct) => Task.CompletedTask;

    public Task ArchiveAsync(Guid goalId, CancellationToken ct)
    {
        Archived.Add(goalId);
        var goal = _store.FirstOrDefault(g => g.Id == goalId);
        if (goal is not null)
        {
            goal.IsArchived = true;
        }

        return Task.CompletedTask;
    }

    public Task AddContributionAsync(Guid goalId, decimal amount, DateOnly date, CancellationToken ct)
    {
        Contributions.Add((goalId, amount, date));
        var goal = _store.FirstOrDefault(g => g.Id == goalId);
        goal?.Contributions.Add(new GoalContribution
        {
            Id = Guid.NewGuid(),
            GoalId = goalId,
            Amount = amount,
            Date = date,
            Source = ContributionSource.Manual,
        });
        return Task.CompletedTask;
    }

    public Task RemoveContributionAsync(Guid contributionId, CancellationToken ct) => Task.CompletedTask;
}

public sealed class FakeFinancialContextBuilder : IFinancialContextBuilder
{
    public Task<FinancialContext> BuildAsync(DateOnly today, CancellationToken ct) =>
        Task.FromResult(new FinancialContext
        {
            MonthlyIncome = 6000m,
            MonthlyFixedExpenses = 2500m,
            MonthlyVariableAvg = 1200m,
            MonthlyVariableStdDev = 150m,
            OtherActiveGoals = [],
            CategoryAverages6m = new Dictionary<string, decimal>(),
            SeasonalityModifiers = new Dictionary<int, decimal>(),
            Today = today,
        });
}

public sealed class FakeGoalFeasibilityEngine : IGoalFeasibilityEngine
{
    public GoalStatus Status { get; set; } = GoalStatus.OnTrack;

    public int SimulateCalls { get; private set; }

    public GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return BuildResult(goal);
    }

    public IReadOnlyList<GoalFeasibilityResult> EvaluateAll(IReadOnlyList<Goal> goals, FinancialContext ctx) =>
        goals.Where(g => !g.IsArchived).Select(BuildResult).ToList();

    public Scenario Simulate(Goal goal, FinancialContext ctx, decimal monthlySaving)
    {
        SimulateCalls++;
        return new Scenario("Symulacja", monthlySaving, new DateOnly(2027, 6, 1), Status);
    }

    private GoalFeasibilityResult BuildResult(Goal goal) => new()
    {
        GoalId = goal.Id,
        Status = Status,
        EffectiveTarget = goal.TargetAmount,
        ProjectedDate = new DateOnly(2027, 1, 1),
        RequiredMonthlySaving = 200m,
        CurrentMonthlySaving = 50m,
        ConfidenceScore = 0.8m,
        AlternativeScenarios = [new Scenario("Current pace", 50m, new DateOnly(2028, 1, 1), GoalStatus.AtRisk)],
        Risks = [],
        DiagnosticSummary = "",
    };
}
