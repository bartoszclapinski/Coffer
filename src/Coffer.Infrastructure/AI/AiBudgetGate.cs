using Coffer.Core.Ai;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Enforces the monthly PLN cap (doc 04). When month-to-date spend plus the prospective
/// call's estimate would exceed the cap, <see cref="AiPriority.Critical"/> work is still
/// allowed (categorisation during an import the user just asked for) but logged as a
/// warning; <see cref="AiPriority.Normal"/> work is blocked.
/// </summary>
public sealed class AiBudgetGate : IAiBudgetGate
{
    private readonly IAiUsageLedger _ledger;
    private readonly IAiSettings _settings;
    private readonly ILogger<AiBudgetGate> _logger;

    public AiBudgetGate(IAiUsageLedger ledger, IAiSettings settings, ILogger<AiBudgetGate> logger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _ledger = ledger;
        _settings = settings;
        _logger = logger;
    }

    public async Task<bool> CanProceedAsync(decimal estimatedCostPln, AiPriority priority, CancellationToken ct)
    {
        var spent = await _ledger.GetCurrentMonthSpendPlnAsync(ct).ConfigureAwait(false);
        var cap = await _settings.GetMonthlyCapPlnAsync(ct).ConfigureAwait(false);

        if (spent + estimatedCostPln <= cap)
        {
            return true;
        }

        if (priority == AiPriority.Critical)
        {
            _logger.LogWarning(
                "AI monthly cap exceeded but proceeding for Critical work: spent {Spent} PLN + estimate {Estimate} PLN > cap {Cap} PLN",
                spent, estimatedCostPln, cap);
            return true;
        }

        _logger.LogInformation(
            "AI call blocked by budget gate: spent {Spent} PLN + estimate {Estimate} PLN > cap {Cap} PLN",
            spent, estimatedCostPln, cap);
        return false;
    }
}
