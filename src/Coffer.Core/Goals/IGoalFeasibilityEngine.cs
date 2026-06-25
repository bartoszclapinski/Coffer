using Coffer.Core.Domain;

namespace Coffer.Core.Goals;

/// <summary>
/// Evaluates goal feasibility deterministically (doc 07) by dispatching each goal to the
/// <see cref="GoalStrategy"/> for its <see cref="GoalType"/>. The engine calculates; the AI (14-C)
/// only explains these results. <see cref="EvaluateAll"/> wires the cross-goal pull — each goal sees
/// the others as competing for the same free cash — and skips archived goals.
/// </summary>
public interface IGoalFeasibilityEngine
{
    GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx);

    IReadOnlyList<GoalFeasibilityResult> EvaluateAll(IReadOnlyList<Goal> goals, FinancialContext ctx);

    /// <summary>
    /// Projects a goal's payoff date and status at a hypothetical monthly saving (the Doradca
    /// simulator slider). Deterministic and free — drives the live "what if I save X/month" panel.
    /// </summary>
    Scenario Simulate(Goal goal, FinancialContext ctx, decimal monthlySaving);
}
