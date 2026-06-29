namespace Coffer.Core.Goals;

/// <summary>
/// An alternative "what if you saved this much per month" outcome for a goal (doc 07).
/// <see cref="MonthlySaving"/> is <c>decimal</c> (hard rule #1); the engine computes
/// <see cref="ProjectedDate"/> and <see cref="Status"/> deterministically. <see cref="LabelCode"/>
/// is a stable presentation-free code (e.g. <c>CURRENT_PACE</c>); the display layer maps it to a
/// localized caption, so no UI text lives in <c>Coffer.Core</c> (hard rule #3).
/// </summary>
public sealed record Scenario(string LabelCode, decimal MonthlySaving, DateOnly ProjectedDate, GoalStatus Status);
