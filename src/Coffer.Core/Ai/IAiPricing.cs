namespace Coffer.Core.Ai;

/// <summary>Estimated cost of an AI call in both USD (vendor billing) and PLN (the user's cap).</summary>
public sealed record AiCost(decimal Usd, decimal Pln);

/// <summary>
/// Converts token counts into an estimated cost for a given provider + model. Used both
/// before a call (the budget gate's estimate) and after (the ledger's recorded cost), so the
/// two agree on pricing. Pricing is provider-aware: a local provider costs nothing, so the
/// same model name can be free on one provider and billed on another.
/// </summary>
public interface IAiPricing
{
    /// <summary>Cost of exactly these token counts — what the ledger records after a call.</summary>
    AiCost Estimate(string provider, string model, int inputTokens, int outputTokens);

    /// <summary>
    /// The budget gate's estimate before a call whose answer is expected to take up to
    /// <paramref name="answerTokens"/> output tokens. A provider may bill more output than the
    /// answer (e.g. thinking tokens), so an implementation adds that allowance here. The default
    /// prices the answer alone.
    /// </summary>
    AiCost EstimateBeforeCall(string provider, string model, int inputTokens, int answerTokens) =>
        Estimate(provider, model, inputTokens, answerTokens);
}
