namespace Coffer.Core.Ai;

/// <summary>
/// Token accounting for one AI call, carried back so the caller can price it and write
/// the cost ledger (doc 04). Realised divergence from the doc-04 <c>IAiProvider</c>
/// sketch, which returned a bare value: cost tracking needs the usage alongside it.
/// </summary>
public sealed record AiUsage(string Provider, string Model, int InputTokens, int OutputTokens);

/// <summary>The value returned by a provider call together with its <see cref="AiUsage"/>.</summary>
public sealed record AiResult<T>(T Value, AiUsage Usage);
