using Coffer.Core.Domain;

namespace Coffer.Core.Goals;

/// <summary>
/// The fields needed to create a <see cref="Goal"/> from the Doradca page's new-goal form.
/// The id, <c>CreatedAt</c>, and the empty contribution/snapshot lists are assigned by the
/// service. <see cref="TargetAmount"/> is <c>decimal</c> (hard rule #1) and <see cref="Currency"/>
/// is non-null (hard rule #9).
/// </summary>
public sealed record NewGoal(
    string Name,
    GoalType Type,
    decimal TargetAmount,
    string Currency,
    DateOnly TargetDate,
    Priority Priority,
    string? Notes);
