namespace Coffer.Core.Planning;

/// <summary>
/// The prose narration of a <see cref="CashFlowProjection"/> produced by
/// <see cref="ICashFlowExplainer"/>. <see cref="GeneratedByAi"/> distinguishes an LLM narration from
/// the deterministic engine-only summary used whenever the AI is unavailable (over budget, offline,
/// or failing). The text only describes the engine's numbers — it never introduces a figure of its own.
/// </summary>
public sealed record CashFlowExplanation(string Narrative, bool GeneratedByAi);
