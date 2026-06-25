using Coffer.Core.Domain;

namespace Coffer.Core.Goals;

/// <summary>
/// Turns the engine's deterministic <see cref="GoalFeasibilityResult"/>s into a day's
/// <see cref="AdvisorReport"/> (doc 07, "the engine calculates, the AI explains"): per-goal risk
/// sentences and 0–3 cutting suggestions, each grounded in a spending category's comparison against
/// its 6-month average. Implementations are budget-gated and metered, anonymise the prompt
/// (hard rule #7), and never invent numbers. On any failure — over budget, offline, malformed
/// response — they return an engine-only report (<see cref="AdvisorReport.GeneratedByAi"/> false)
/// carrying the deterministic risk text so the advisor always has something to show.
/// </summary>
public interface IAdvisorReportGenerator
{
    Task<AdvisorReport> GenerateAsync(
        IReadOnlyList<GoalFeasibilityResult> results,
        FinancialContext context,
        IReadOnlyList<CategorySpending> categorySpending,
        DateOnly date,
        CancellationToken ct);
}
