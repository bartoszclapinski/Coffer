namespace Coffer.Core.Ai;

/// <summary>
/// Enforces the user's monthly PLN cap (doc 04). Compares month-to-date ledger spend
/// plus the prospective call's estimate against the cap: <see cref="AiPriority.Critical"/>
/// work proceeds even over cap (with a warning), <see cref="AiPriority.Normal"/> work is
/// blocked once the cap would be exceeded.
/// </summary>
public interface IAiBudgetGate
{
    Task<bool> CanProceedAsync(decimal estimatedCostPln, AiPriority priority, CancellationToken ct);
}
