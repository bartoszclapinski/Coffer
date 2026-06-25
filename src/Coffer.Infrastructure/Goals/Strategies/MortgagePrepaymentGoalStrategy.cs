using Coffer.Core.Domain;
using Coffer.Core.Goals;

namespace Coffer.Infrastructure.Goals.Strategies;

/// <summary>
/// Saving toward a one-off mortgage prepayment (doc 07). v1 treats the goal as accumulating the
/// planned prepayment amount — a fixed-target savings feasibility. The shorten-vs-reduce comparison
/// is produced separately by <see cref="IMortgagePrepaymentCalculator"/> from loan terms the UI
/// supplies (those terms are not stored on the <see cref="Goal"/>), and the engine recommends
/// neither mode (avoids the "advisor that recommends" pitfall).
/// </summary>
public sealed class MortgagePrepaymentGoalStrategy : GoalStrategy
{
    public override GoalType Type => GoalType.MortgagePrepayment;

    public override GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return EvaluateSavingsGoal(goal, ctx, goal.TargetAmount);
    }
}
