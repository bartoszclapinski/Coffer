using Coffer.Core.Ai;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Static per-model token pricing (doc 04 cost discipline). Rates are USD per million
/// tokens; PLN is a fixed-rate conversion — good enough for a budget guard, not
/// accounting. Unknown models fall back to the more expensive Sonnet rate so an estimate
/// never under-reports.
/// </summary>
public sealed class AiPricing : IAiPricing
{
    // Approximate USD per 1M tokens (input, output).
    private static readonly Dictionary<string, (decimal Input, decimal Output)> _rates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-haiku-4-5"] = (1.00m, 5.00m),
            ["claude-sonnet-4-6"] = (3.00m, 15.00m),
            ["gpt-4o-mini"] = (0.15m, 0.60m),
            ["gpt-4o"] = (2.50m, 10.00m),
        };

    private static readonly (decimal Input, decimal Output) _fallback = (3.00m, 15.00m);

    private const decimal _usdToPln = 4.00m;
    private const decimal _perMillion = 1_000_000m;

    public AiCost Estimate(string model, int inputTokens, int outputTokens)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);

        var (inputRate, outputRate) = _rates.TryGetValue(model, out var r) ? r : _fallback;

        var usd = ((inputTokens * inputRate) + (outputTokens * outputRate)) / _perMillion;
        var pln = usd * _usdToPln;
        return new AiCost(usd, pln);
    }
}
