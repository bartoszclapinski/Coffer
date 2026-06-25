using Coffer.Core.Goals;

namespace Coffer.Core.Domain;

/// <summary>
/// Money credited toward a <see cref="Goal"/> (doc 07). <see cref="Amount"/> is <c>decimal</c>
/// (hard rule #1); <see cref="Date"/> is the contribution's transaction-scale
/// <see cref="DateOnly"/>. <see cref="TransactionId"/> is set only when the contribution is
/// linked to a real transaction (<see cref="ContributionSource.LinkedTransaction"/> /
/// <see cref="ContributionSource.Tag"/>).
/// </summary>
public class GoalContribution
{
    public Guid Id { get; set; }

    public Guid GoalId { get; set; }

    public decimal Amount { get; set; }

    public DateOnly Date { get; set; }

    public ContributionSource Source { get; set; }

    public Guid? TransactionId { get; set; }
}
