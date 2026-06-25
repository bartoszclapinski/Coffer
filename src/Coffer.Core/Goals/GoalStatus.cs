namespace Coffer.Core.Goals;

/// <summary>
/// The feasibility verdict for a goal at a point in time (doc 07). Computed by the
/// deterministic engine, never the LLM. Persisted as the enum name on <see cref="Domain.GoalSnapshot"/>.
/// </summary>
public enum GoalStatus
{
    /// <summary>Required monthly saving comfortably below available free cash.</summary>
    OnTrack,

    /// <summary>Reachable but tight — required saving approaches free cash.</summary>
    NeedsAttention,

    /// <summary>Required saving exceeds free cash; the target date is unlikely.</summary>
    AtRisk,

    /// <summary>The target date has already passed with the goal unmet.</summary>
    Late,

    /// <summary>The target amount has been reached.</summary>
    Achieved,

    /// <summary>Temporarily excluded from active tracking by the owner.</summary>
    Paused,
}
