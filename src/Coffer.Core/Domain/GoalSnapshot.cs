using Coffer.Core.Goals;

namespace Coffer.Core.Domain;

/// <summary>
/// The audit log of how a <see cref="Goal"/>'s projection changed over time (doc 07): one row per
/// goal per day, written by the daily snapshot job. Lets the UI answer "30 days ago this was
/// OnTrack, now it's Late — what changed?". Money is <c>decimal</c> (hard rule #1);
/// <see cref="Date"/> / <see cref="ProjectedDate"/> are <see cref="DateOnly"/>.
/// <see cref="ConfidenceScore"/> is 0..1.
/// </summary>
public class GoalSnapshot
{
    public Guid Id { get; set; }

    public Guid GoalId { get; set; }

    public DateOnly Date { get; set; }

    public decimal CurrentAmount { get; set; }

    public decimal MonthlySaving { get; set; }

    public DateOnly ProjectedDate { get; set; }

    public GoalStatus Status { get; set; }

    public decimal ConfidenceScore { get; set; }
}
