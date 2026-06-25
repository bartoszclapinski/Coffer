using Coffer.Core.Domain;
using Coffer.Core.Goals;

namespace Coffer.Infrastructure.Goals.Strategies;

/// <summary>
/// Deliberately limited (doc 07): tracks the free cash available to commit toward investing and
/// progress against a stated target. It never predicts returns, recommends instruments, or suggests
/// rebalancing — that is licensed-advice territory the app stays out of. Feasibility is therefore the
/// same straight-line savings calculation as any other fixed-target goal.
/// </summary>
public sealed class InvestmentGoalStrategy : GoalStrategy
{
    public override GoalType Type => GoalType.Investment;

    public override GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return EvaluateSavingsGoal(goal, ctx, goal.TargetAmount);
    }
}
