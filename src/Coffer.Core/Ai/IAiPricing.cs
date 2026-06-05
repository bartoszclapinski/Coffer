namespace Coffer.Core.Ai;

/// <summary>Estimated cost of an AI call in both USD (vendor billing) and PLN (the user's cap).</summary>
public sealed record AiCost(decimal Usd, decimal Pln);

/// <summary>
/// Converts token counts into an estimated cost for a given model. Used both before a
/// call (the budget gate's estimate) and after (the ledger's recorded cost), so the two
/// agree on pricing.
/// </summary>
public interface IAiPricing
{
    AiCost Estimate(string model, int inputTokens, int outputTokens);
}
