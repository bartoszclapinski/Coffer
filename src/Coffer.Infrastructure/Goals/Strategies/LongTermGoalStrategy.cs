using Coffer.Core.Domain;
using Coffer.Core.Goals;

namespace Coffer.Infrastructure.Goals.Strategies;

/// <summary>A distant fixed-target goal (e.g. a child's education) saved toward straight-line by its date (doc 07).</summary>
public sealed class LongTermGoalStrategy : GoalStrategy
{
    public override GoalType Type => GoalType.LongTerm;

    public override GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return EvaluateSavingsGoal(goal, ctx, goal.TargetAmount);
    }
}
