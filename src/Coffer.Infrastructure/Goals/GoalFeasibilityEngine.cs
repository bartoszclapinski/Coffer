using Coffer.Core.Domain;
using Coffer.Core.Goals;

namespace Coffer.Infrastructure.Goals;

/// <summary>
/// Dispatches each goal to the <see cref="GoalStrategy"/> registered for its <see cref="GoalType"/>
/// (doc 07). All numbers are the engine's; the AI only explains them later. <see cref="EvaluateAll"/>
/// gives each goal a context whose <see cref="FinancialContext.OtherActiveGoals"/> are the other
/// active goals, so their pull on shared free cash is reflected, and skips archived goals entirely.
/// </summary>
public sealed class GoalFeasibilityEngine : IGoalFeasibilityEngine
{
    private readonly IReadOnlyDictionary<GoalType, GoalStrategy> _strategies;

    public GoalFeasibilityEngine(IEnumerable<GoalStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        _strategies = strategies.ToDictionary(s => s.Type);
    }

    public GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(ctx);

        if (!_strategies.TryGetValue(goal.Type, out var strategy))
        {
            throw new InvalidOperationException($"No goal strategy registered for type '{goal.Type}'.");
        }

        return strategy.Evaluate(goal, ctx);
    }

    public IReadOnlyList<GoalFeasibilityResult> EvaluateAll(IReadOnlyList<Goal> goals, FinancialContext ctx)
    {
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(ctx);

        var active = goals.Where(g => !g.IsArchived).ToList();
        var results = new List<GoalFeasibilityResult>(active.Count);

        foreach (var goal in active)
        {
            var others = active.Where(g => g.Id != goal.Id).ToList();
            var scopedCtx = ctx with { OtherActiveGoals = others };
            results.Add(Evaluate(goal, scopedCtx));
        }

        return results;
    }
}
