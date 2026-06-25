using Coffer.Core.Domain;
using Coffer.Core.Goals;

namespace Coffer.Infrastructure.Goals.Strategies;

/// <summary>A known large outlay (renovation, wedding) saved toward a fixed target by a date (doc 07).</summary>
public sealed class LargeExpenseGoalStrategy : GoalStrategy
{
    public override GoalType Type => GoalType.LargeExpense;

    public override GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return EvaluateSavingsGoal(goal, ctx, goal.TargetAmount);
    }
}
