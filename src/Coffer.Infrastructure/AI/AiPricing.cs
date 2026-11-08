using Coffer.Core.Ai;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Static per-provider, per-model token pricing (doc 04 cost discipline). Rates are USD per
/// million tokens; PLN is a fixed-rate conversion — good enough for a budget guard, not
/// accounting. <see cref="AiDefaults.OllamaProvider"/> runs locally and costs nothing. For Claude,
/// unknown models fall back to the most expensive Claude rate in the table (Sonnet 4.6), and so
/// does an unknown provider, so the rate never under-reports. Claude bills thinking tokens as
/// output, so <see cref="EstimateBeforeCall"/> adds the full
/// <see cref="ClaudeModelRules.ThinkingHeadroomTokens"/> for Claude models that think by default:
/// the pre-call estimate covers the most thinking a call can spend, and the ledger records the real
/// usage afterwards.
/// </summary>
public sealed class AiPricing : IAiPricing
{
    // Approximate USD per 1M tokens (input, output). The 4.x models stay priced so a stored
    // choice keeps being estimated correctly after the defaults moved to the 5.5 generation.
    private static readonly Dictionary<string, (decimal Input, decimal Output)> _rates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-haiku-5-5"] = (0.10m, 0.50m),
            ["claude-sonnet-5-5"] = (2.00m, 10.00m),
            ["claude-haiku-4-5"] = (1.00m, 5.00m),
            ["claude-sonnet-4-6"] = (3.00m, 15.00m),
        };

    // Haiku 5.5 bills a prompt over 100K input tokens on a second, 5x rate card.
    private const string HaikuFiveFive = "claude-haiku-5-5";
    private const int HaikuLongPromptThreshold = 100_000;
    private static readonly (decimal Input, decimal Output) _haikuLongPromptRate = (0.50m, 2.50m);

    private static readonly (decimal Input, decimal Output) _fallback = (3.00m, 15.00m);

    private const decimal UsdToPln = 4.00m;
    private const decimal PerMillion = 1_000_000m;

    public AiCost Estimate(string provider, string model, int inputTokens, int outputTokens)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        ArgumentException.ThrowIfNullOrEmpty(model);

        if (IsLocal(provider))
        {
            return new AiCost(0m, 0m);
        }

        var (inputRate, outputRate) = IsClaude(provider) ? ClaudeRateFor(model, inputTokens) : _fallback;

        var usd = ((inputTokens * inputRate) + (outputTokens * outputRate)) / PerMillion;
        var pln = usd * UsdToPln;
        return new AiCost(usd, pln);
    }

    public AiCost EstimateBeforeCall(string provider, string model, int inputTokens, int answerTokens)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        ArgumentException.ThrowIfNullOrEmpty(model);

        // The same output cap ClaudeProvider sends: the answer plus thinking headroom where it applies.
        var outputTokens = IsClaude(provider) ? ClaudeModelRules.MaxTokensFor(model, answerTokens) : answerTokens;
        return Estimate(provider, model, inputTokens, outputTokens);
    }

    private static bool IsLocal(string provider) =>
        string.Equals(provider, AiDefaults.OllamaProvider, StringComparison.OrdinalIgnoreCase);

    private static bool IsClaude(string provider) =>
        string.Equals(provider, AiDefaults.ClaudeProvider, StringComparison.OrdinalIgnoreCase);

    private static (decimal Input, decimal Output) ClaudeRateFor(string model, int inputTokens)
    {
        if (string.Equals(model, HaikuFiveFive, StringComparison.OrdinalIgnoreCase)
            && inputTokens > HaikuLongPromptThreshold)
        {
            return _haikuLongPromptRate;
        }

        return _rates.TryGetValue(model, out var rate) ? rate : _fallback;
    }
}
