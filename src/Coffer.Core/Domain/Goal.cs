using Coffer.Core.Goals;

namespace Coffer.Core.Domain;

/// <summary>
/// A savings goal the advisor tracks (doc 07). <see cref="TargetAmount"/> is <c>decimal</c>
/// (hard rule #1); <see cref="TargetDate"/> is a transaction-scale <see cref="DateOnly"/> and
/// <see cref="CreatedAt"/> is a UTC system timestamp (hard rule #2). <see cref="Currency"/> is
/// non-null (hard rule #9). The feasibility engine reads this plus its
/// <see cref="Contributions"/> and writes <see cref="Snapshots"/> over time.
/// </summary>
public class Goal
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public GoalType Type { get; set; }

    public decimal TargetAmount { get; set; }

    public string Currency { get; set; } = "PLN";

    public DateOnly TargetDate { get; set; }

    public Priority Priority { get; set; }

    public string? Notes { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<GoalContribution> Contributions { get; set; } = [];

    public List<GoalSnapshot> Snapshots { get; set; } = [];
}
