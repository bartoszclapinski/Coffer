namespace Coffer.Core.Planning;

/// <summary>
/// A stretch of calendar days for one account that no imported statement period covers. A gap means
/// the running-sum opening balance is unreliable, so the planner warns the owner to import the
/// missing statement. <see cref="From"/>/<see cref="To"/> are inclusive.
/// </summary>
public sealed record StatementGap(Guid AccountId, DateOnly From, DateOnly To);
