namespace Coffer.Core.Planning;

/// <summary>
/// Proposes <see cref="RecurringFlowCandidate"/>s from the transaction history (recurring merchants
/// with a stable cadence). Detection only suggests — the owner confirms and sets the accrual offset.
/// </summary>
public interface IRecurringFlowDetector
{
    Task<IReadOnlyList<RecurringFlowCandidate>> DetectAsync(CancellationToken ct);
}
