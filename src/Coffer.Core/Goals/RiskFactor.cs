namespace Coffer.Core.Goals;

/// <summary>
/// A deterministic risk the engine flags for a goal (doc 07). <see cref="Code"/> is a stable
/// machine key (e.g. <c>tight-timeline</c>, <c>high-variability</c>); <see cref="Description"/>
/// is the templated Polish sentence shown until the 14-C LLM commentary rewrites it.
/// </summary>
public sealed record RiskFactor(string Code, string Description);
