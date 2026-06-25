namespace Coffer.Core.Goals;

/// <summary>
/// How a <see cref="Domain.GoalContribution"/> came to credit a goal (doc 07). Only
/// <see cref="Manual"/> and <see cref="Tag"/> are wired in v1; <see cref="LinkedTransaction"/>
/// and <see cref="AutoDetect"/> are modelled for the later savings-account linkage follow-up.
/// </summary>
public enum ContributionSource
{
    /// <summary>The owner added the contribution by hand.</summary>
    Manual,

    /// <summary>A specific transaction was linked to the goal.</summary>
    LinkedTransaction,

    /// <summary>A transaction tagged <c>goal:&lt;name&gt;</c> credited the goal automatically.</summary>
    Tag,

    /// <summary>A transfer to a savings sub-account known to back the goal (not wired in v1).</summary>
    AutoDetect,
}
