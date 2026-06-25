namespace Coffer.Core.Goals;

/// <summary>
/// The multipliers that tune how boldly the engine apportions free cash and flags risk
/// (doc 07, "Aggressiveness profile"). v1 ships <see cref="Balanced"/> only; Conservative and
/// Aggressive are a deferred follow-up (sprint-14 plan), which is exactly why every strategy
/// takes this as an input rather than hard-coding the buffer — the other two profiles drop in
/// later without touching strategy logic.
/// </summary>
/// <param name="FreeCashUtilisation">Fraction of raw free cash usable toward goals (Conservative ~0.80, Aggressive ~0.95).</param>
/// <param name="OnTrackHeadroom">A goal is <see cref="GoalStatus.OnTrack"/> when required saving ≤ usable free cash × this.</param>
public sealed record AggressivenessProfile(decimal FreeCashUtilisation, decimal OnTrackHeadroom)
{
    /// <summary>The v1 default: use 85% of free cash, OnTrack while required saving is ≤ half of that.</summary>
    public static AggressivenessProfile Balanced { get; } = new(0.85m, 0.5m);
}
