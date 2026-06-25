namespace Coffer.Core.Goals;

/// <summary>
/// The once-a-day advisor refresh (doc 07): evaluates every active goal, writes a
/// <see cref="Domain.GoalSnapshot"/> row per goal so the UI can show how a projection drifted over
/// time, and regenerates the day's <see cref="Domain.AdvisorReport"/> (the only place the LLM is
/// called — never on a UI refresh). Idempotent within a day: a second run on a date that already has
/// snapshots is a no-op, so the desktop startup hook can fire it on every launch.
/// </summary>
public interface IGoalSnapshotJob
{
    /// <summary>Returns the number of <see cref="Domain.GoalSnapshot"/> rows written (0 if it already ran today).</summary>
    Task<int> RunAsync(DateOnly today, CancellationToken ct);
}
