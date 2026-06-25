namespace Coffer.Core.Goals;

/// <summary>
/// The deterministic verdict for one goal (doc 07): every number here comes from the engine, never
/// the LLM. <see cref="DiagnosticSummary"/> is a compact, non-user-facing string fed to the 14-C
/// commentator as grounding. <see cref="GoalId"/> lets <see cref="GoalFeasibilityEngine.EvaluateAll"/>
/// map results back to goals. Money is <c>decimal</c> (hard rule #1); <see cref="ConfidenceScore"/>
/// is 0..1.
/// </summary>
public sealed record GoalFeasibilityResult
{
    public required Guid GoalId { get; init; }

    public required GoalStatus Status { get; init; }

    /// <summary>
    /// The target the engine actually evaluated against — equals <see cref="Domain.Goal.TargetAmount"/>
    /// for most goals, but an emergency fund derives it from current expenses, so the UI shows this
    /// rather than the stored amount. Defaults to 0 for results that predate the field.
    /// </summary>
    public decimal EffectiveTarget { get; init; }

    public required DateOnly ProjectedDate { get; init; }

    public required decimal RequiredMonthlySaving { get; init; }

    public required decimal CurrentMonthlySaving { get; init; }

    public required decimal ConfidenceScore { get; init; }

    public required IReadOnlyList<Scenario> AlternativeScenarios { get; init; }

    public required IReadOnlyList<RiskFactor> Risks { get; init; }

    public required string DiagnosticSummary { get; init; }
}
