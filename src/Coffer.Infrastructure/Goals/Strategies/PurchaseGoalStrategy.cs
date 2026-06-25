using Coffer.Core.Domain;
using Coffer.Core.Goals;

namespace Coffer.Infrastructure.Goals.Strategies;

/// <summary>A fixed-target purchase: save the sticker price by the chosen date (doc 07).</summary>
public sealed class PurchaseGoalStrategy : GoalStrategy
{
    public override GoalType Type => GoalType.Purchase;

    public override GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return EvaluateSavingsGoal(goal, ctx, goal.TargetAmount);
    }
}
