using Coffer.Core.Domain;

namespace Coffer.Core.Goals;

/// <summary>
/// The deterministic financial picture a <see cref="GoalStrategy"/> evaluates a goal against
/// (doc 07), derived from the transaction history by the context builder. All money is
/// <c>decimal</c> (hard rule #1). <see cref="MonthlyVariableAvg"/> / <see cref="CategoryAverages6m"/>
/// are 6-month moving averages. <see cref="SeasonalityModifiers"/> is keyed by calendar month
/// (1–12) and defaults to a neutral 1.0 in v1 (the real per-month model is a deferred follow-up).
/// <see cref="OtherActiveGoals"/> excludes the goal under evaluation (set per goal by the engine).
/// </summary>
public sealed record FinancialContext
{
    public required decimal MonthlyIncome { get; init; }

    public required decimal MonthlyFixedExpenses { get; init; }

    public required decimal MonthlyVariableAvg { get; init; }

    public required decimal MonthlyVariableStdDev { get; init; }

    public required IReadOnlyList<Goal> OtherActiveGoals { get; init; }

    public required IReadOnlyDictionary<string, decimal> CategoryAverages6m { get; init; }

    public required IReadOnlyDictionary<int, decimal> SeasonalityModifiers { get; init; }

    public required DateOnly Today { get; init; }

    /// <summary>v1 ships <see cref="AggressivenessProfile.Balanced"/> only.</summary>
    public AggressivenessProfile Profile { get; init; } = AggressivenessProfile.Balanced;

    /// <summary>The seasonality modifier for a calendar month, defaulting to a neutral 1.0.</summary>
    public decimal SeasonalityFor(int month) =>
        SeasonalityModifiers.TryGetValue(month, out var m) ? m : 1.0m;
}
