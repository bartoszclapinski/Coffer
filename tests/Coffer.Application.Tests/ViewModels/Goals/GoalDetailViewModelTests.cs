using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Goals;
using Coffer.Core.Domain;
using Coffer.Core.Goals;
using FluentAssertions;

namespace Coffer.Application.Tests.ViewModels.Goals;

public class GoalDetailViewModelTests
{
    [Fact]
    public void Constructor_FormatsEngineFiguresAsPolishStrings()
    {
        var (goal, result, context, engine) = Build();

        var vm = new GoalDetailViewModel(goal, result, context, engine, new FakeLocalizer(), _ => Task.CompletedTask, (_, _, _) => Task.CompletedTask);

        vm.StatusText.Should().Be("Goal.Status.OnTrack");
        vm.TargetText.Should().Contain("zł");
        vm.TypeText.Should().Be("Goal.Type.Purchase");
        vm.Scenarios.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_ResolvesScenarioLabelAndRiskTextThroughLocalizer()
    {
        var (goal, result, context, engine) = Build();
        result = result with
        {
            Risks = [new RiskFactor("NO_FREE_CASH", "raw english fallback")],
        };

        var vm = new GoalDetailViewModel(goal, result, context, engine, new FakeLocalizer(), _ => Task.CompletedTask, (_, _, _) => Task.CompletedTask);

        vm.Scenarios[0].Label.Should().Be("Goal.Scenario.CurrentPace");
        vm.Risks.Should().ContainSingle().Which.Should().Be("Goal.Risk.NoFreeCash");
    }

    [Fact]
    public void Constructor_UnmappedRiskCode_FallsBackToDescription()
    {
        var (goal, result, context, engine) = Build();
        result = result with
        {
            Risks = [new RiskFactor("UNKNOWN_CODE", "raw english fallback")],
        };

        var vm = new GoalDetailViewModel(goal, result, context, engine, new FakeLocalizer(), _ => Task.CompletedTask, (_, _, _) => Task.CompletedTask);

        vm.Risks.Should().ContainSingle().Which.Should().Be("raw english fallback");
    }

    [Fact]
    public void Constructor_BuildsProjectionAndSimulationFromRequiredPace()
    {
        var (goal, result, context, engine) = Build();

        var vm = new GoalDetailViewModel(goal, result, context, engine, new FakeLocalizer(), _ => Task.CompletedTask, (_, _, _) => Task.CompletedTask);

        vm.MonthlySavingInput.Should().Be(200m);
        engine.SimulateCalls.Should().BeGreaterThan(0);
        vm.ProjectionSeries.Should().HaveCount(2);
        vm.SimulatedProjectedDateText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ChangingMonthlySaving_RerunsSimulation()
    {
        var (goal, result, context, engine) = Build();
        var vm = new GoalDetailViewModel(goal, result, context, engine, new FakeLocalizer(), _ => Task.CompletedTask, (_, _, _) => Task.CompletedTask);
        var before = engine.SimulateCalls;

        vm.MonthlySavingInput = 750m;

        engine.SimulateCalls.Should().Be(before + 1);
    }

    [Fact]
    public async Task AddContribution_WithZeroAmount_DoesNotInvokeCallback()
    {
        var (goal, result, context, engine) = Build();
        var calls = 0;
        var vm = new GoalDetailViewModel(goal, result, context, engine, new FakeLocalizer(), _ => Task.CompletedTask, (_, _, _) =>
        {
            calls++;
            return Task.CompletedTask;
        });

        vm.ContributionAmount = 0m;
        await vm.AddContributionCommand.ExecuteAsync(null);

        calls.Should().Be(0);
    }

    [Fact]
    public async Task AddContribution_WithPositiveAmount_InvokesCallback()
    {
        var (goal, result, context, engine) = Build();
        var captured = 0m;
        var vm = new GoalDetailViewModel(goal, result, context, engine, new FakeLocalizer(), _ => Task.CompletedTask, (_, amount, _) =>
        {
            captured = amount;
            return Task.CompletedTask;
        });

        vm.ContributionAmount = 320m;
        await vm.AddContributionCommand.ExecuteAsync(null);

        captured.Should().Be(320m);
    }

    private static (Goal, GoalFeasibilityResult, FinancialContext, FakeGoalFeasibilityEngine) Build()
    {
        var goal = new Goal
        {
            Id = Guid.NewGuid(),
            Name = "Wakacje Grecja",
            Type = GoalType.Purchase,
            TargetAmount = 8000m,
            Currency = "PLN",
            TargetDate = new DateOnly(2027, 7, 1),
            Priority = Priority.High,
            CreatedAt = DateTime.UtcNow,
        };

        var result = new GoalFeasibilityResult
        {
            GoalId = goal.Id,
            Status = GoalStatus.OnTrack,
            EffectiveTarget = 8000m,
            ProjectedDate = new DateOnly(2027, 5, 1),
            RequiredMonthlySaving = 200m,
            CurrentMonthlySaving = 50m,
            ConfidenceScore = 0.8m,
            AlternativeScenarios = [new Scenario("CURRENT_PACE", 50m, new DateOnly(2028, 1, 1), GoalStatus.AtRisk)],
            Risks = [],
            DiagnosticSummary = "",
        };

        var context = new FinancialContext
        {
            MonthlyIncome = 6000m,
            MonthlyFixedExpenses = 2500m,
            MonthlyVariableAvg = 1200m,
            MonthlyVariableStdDev = 150m,
            OtherActiveGoals = [],
            CategoryAverages6m = new Dictionary<string, decimal>(),
            SeasonalityModifiers = new Dictionary<int, decimal>(),
            Today = new DateOnly(2026, 7, 1),
        };

        return (goal, result, context, new FakeGoalFeasibilityEngine());
    }
}
