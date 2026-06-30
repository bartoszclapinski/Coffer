using Coffer.Core.Domain;
using Coffer.Core.Goals;

namespace Coffer.Infrastructure.Goals.Strategies;

/// <summary>
/// An emergency fund sized as a multiple of monthly spend (doc 07). The target grows with the cost
/// of living: it is the larger of the owner's stated amount and six months of current expenses, so
/// when spending rises the fund's target rises with it.
/// </summary>
public sealed class EmergencyFundGoalStrategy : GoalStrategy
{
    private const int MonthsOfExpenses = 6;

    public override GoalType Type => GoalType.EmergencyFund;

    public override GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(ctx);

        return EvaluateSavingsGoal(goal, ctx, ResolveTargetAmount(goal, ctx));
    }

    public override decimal ResolveTargetAmount(Goal goal, FinancialContext ctx)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(ctx);

        var monthlyExpenses = ctx.MonthlyFixedExpenses + ctx.MonthlyVariableAvg;
        return Math.Max(goal.TargetAmount, MonthsOfExpenses * monthlyExpenses);
    }
}
