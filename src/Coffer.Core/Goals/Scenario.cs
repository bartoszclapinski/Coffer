namespace Coffer.Core.Goals;

/// <summary>
/// An alternative "what if you saved this much per month" outcome for a goal (doc 07).
/// <see cref="MonthlySaving"/> is <c>decimal</c> (hard rule #1); the engine computes
/// <see cref="ProjectedDate"/> and <see cref="Status"/> deterministically. <see cref="Label"/>
/// is a short Polish caption for the UI.
/// </summary>
public sealed record Scenario(string Label, decimal MonthlySaving, DateOnly ProjectedDate, GoalStatus Status);
