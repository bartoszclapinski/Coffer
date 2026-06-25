using Coffer.Core.Domain;
using Coffer.Core.Goals;
using Coffer.Infrastructure.Goals;
using Coffer.Infrastructure.Goals.Strategies;
using FluentAssertions;
using FsCheck.Xunit;

namespace Coffer.Infrastructure.Tests.Goals;

public class GoalFeasibilityEngineTests
{
    private static readonly DateOnly _today = new(2026, 1, 1);
    private static readonly DateOnly _inAYear = new(2027, 1, 1);

    private static IGoalFeasibilityEngine NewEngine() =>
        new GoalFeasibilityEngine(
        [
            new PurchaseGoalStrategy(),
            new LargeExpenseGoalStrategy(),
            new EmergencyFundGoalStrategy(),
            new MortgagePrepaymentGoalStrategy(),
            new InvestmentGoalStrategy(),
            new LongTermGoalStrategy(),
        ]);

    private static FinancialContext Ctx(
        decimal income,
        decimal fixedExpenses,
        decimal variableAvg,
        decimal variableStdDev = 0m,
        IReadOnlyList<Goal>? others = null) =>
        new()
        {
            MonthlyIncome = income,
            MonthlyFixedExpenses = fixedExpenses,
            MonthlyVariableAvg = variableAvg,
            MonthlyVariableStdDev = variableStdDev,
            OtherActiveGoals = others ?? [],
            CategoryAverages6m = new Dictionary<string, decimal>(),
            SeasonalityModifiers = new Dictionary<int, decimal>(),
            Today = _today,
        };

    private static Goal Goal(
        decimal target,
        GoalType type = GoalType.Purchase,
        decimal saved = 0m,
        DateOnly? targetDate = null,
        bool archived = false)
    {
        var goal = new Goal
        {
            Id = Guid.NewGuid(),
            Name = type.ToString(),
            Type = type,
            TargetAmount = target,
            Currency = "PLN",
            TargetDate = targetDate ?? _inAYear,
            Priority = Priority.Medium,
            IsArchived = archived,
            CreatedAt = DateTime.UtcNow,
        };

        if (saved > 0m)
        {
            goal.Contributions.Add(new GoalContribution
            {
                Id = Guid.NewGuid(),
                GoalId = goal.Id,
                Amount = saved,
                Date = _today,
                Source = ContributionSource.Manual,
            });
        }

        return goal;
    }

    [Theory]
    // 12000 over 12 months ⇒ 1000/mo required. Free cash and the Balanced profile decide the verdict.
    [InlineData(5000, 2000, 600, GoalStatus.OnTrack)]        // usable 2040, headroom 1020 ≥ 1000
    [InlineData(4000, 1800, 435, GoalStatus.NeedsAttention)] // usable ~1500: 750 < 1000 ≤ 1500
    [InlineData(3000, 1800, 200, GoalStatus.AtRisk)]         // usable 850 < 1000
    public void Evaluate_PurchaseStatusTable(decimal income, decimal fixedExp, decimal varAvg, GoalStatus expected)
    {
        var result = NewEngine().Evaluate(Goal(12_000m), Ctx(income, fixedExp, varAvg));

        result.RequiredMonthlySaving.Should().Be(1000m);
        result.Status.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_AchievedWhenTargetReached()
    {
        var result = NewEngine().Evaluate(Goal(12_000m, saved: 12_000m), Ctx(5000, 2000, 600));

        result.Status.Should().Be(GoalStatus.Achieved);
        result.ProjectedDate.Should().Be(_today);
    }

    [Fact]
    public void Evaluate_LateWhenTargetDatePassedWithMoneyOutstanding()
    {
        var result = NewEngine().Evaluate(
            Goal(12_000m, targetDate: new DateOnly(2025, 6, 1)),
            Ctx(5000, 2000, 600));

        result.Status.Should().Be(GoalStatus.Late);
        result.Risks.Should().Contain(r => r.Code == "PAST_TARGET_DATE");
    }

    [Fact]
    public void Evaluate_EmergencyFund_TargetIsSixMonthsOfExpenses()
    {
        // Stated target is tiny; the strategy lifts it to 6 × (fixed + variable) = 6 × 3000 = 18000,
        // so the required monthly saving over 12 months is 1500, not 1000/12.
        var result = NewEngine().Evaluate(
            Goal(1_000m, GoalType.EmergencyFund),
            Ctx(income: 8000, fixedExpenses: 2000, variableAvg: 1000));

        result.RequiredMonthlySaving.Should().Be(1500m);
    }

    [Fact]
    public void Evaluate_EmergencyFund_EffectiveTargetIsSixMonthsOfExpenses()
    {
        var result = NewEngine().Evaluate(
            Goal(1_000m, GoalType.EmergencyFund),
            Ctx(income: 8000, fixedExpenses: 2000, variableAvg: 1000));

        result.EffectiveTarget.Should().Be(18_000m, "the strategy lifts the stated target to 6 × (fixed + variable)");
    }

    [Fact]
    public void Simulate_HigherMonthlySaving_ReachesGoalNoLater()
    {
        var engine = NewEngine();
        var goal = Goal(12_000m);
        var ctx = Ctx(5000, 2000, 600);

        var slow = engine.Simulate(goal, ctx, 500m);
        var fast = engine.Simulate(goal, ctx, 2000m);

        fast.MonthlySaving.Should().Be(2000m);
        fast.ProjectedDate.Should().BeOnOrBefore(slow.ProjectedDate);
    }

    [Fact]
    public void Simulate_UnregisteredType_Throws()
    {
        var engine = new GoalFeasibilityEngine([new PurchaseGoalStrategy()]);

        var act = () => engine.Simulate(Goal(1_000m, GoalType.Investment), Ctx(5000, 2000, 600), 500m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EvaluateAll_SkipsArchivedGoals()
    {
        var active = Goal(12_000m);
        var archived = Goal(5_000m, archived: true);

        var results = NewEngine().EvaluateAll([active, archived], Ctx(5000, 2000, 600));

        results.Should().ContainSingle();
        results[0].GoalId.Should().Be(active.Id);
    }

    [Fact]
    public void EvaluateAll_OtherGoalsCompeteForTheSameFreeCash()
    {
        var ctx = Ctx(5000, 2000, 600); // usable 2040 alone
        var goal = Goal(12_000m);
        var competitor = Goal(60_000m); // pulls ~5000/mo, swallowing the free cash

        var alone = NewEngine().Evaluate(goal, ctx);
        var together = NewEngine().EvaluateAll([goal, competitor], ctx)
            .Single(r => r.GoalId == goal.Id);

        alone.Status.Should().Be(GoalStatus.OnTrack);
        together.Status.Should().Be(GoalStatus.AtRisk);
    }

    [Fact]
    public void Evaluate_UnregisteredType_Throws()
    {
        var engine = new GoalFeasibilityEngine([new PurchaseGoalStrategy()]);

        var act = () => engine.Evaluate(Goal(1_000m, GoalType.Investment), Ctx(5000, 2000, 600));

        act.Should().Throw<InvalidOperationException>();
    }

    [Property]
    public bool Confidence_AlwaysWithinBounds(decimal income, decimal fixedExp, decimal varAvg, decimal target)
    {
        var ctx = Ctx(Clamp(income), Clamp(fixedExp), Clamp(varAvg));
        var result = NewEngine().Evaluate(Goal(Clamp(target) + 1m), ctx);

        return result.ConfidenceScore >= 0.05m && result.ConfidenceScore <= 0.98m;
    }

    [Property]
    public bool MoreSaved_NeverPushesProjectedDateLater(decimal target, decimal savedLess, decimal extra)
    {
        var t = Clamp(target) + 5000m;
        var less = Math.Min(Clamp(savedLess), t);
        var more = Math.Min(less + Math.Abs(Clamp(extra)) + 1m, t);
        var ctx = Ctx(5000, 2000, 600);

        var lessResult = NewEngine().Evaluate(Goal(t, saved: less), ctx);
        var moreResult = NewEngine().Evaluate(Goal(t, saved: more), ctx);

        return moreResult.ProjectedDate <= lessResult.ProjectedDate;
    }

    private static decimal Clamp(decimal v) => Math.Round(Math.Clamp(Math.Abs(v), 0m, 1_000_000m), 2);
}
