namespace Coffer.Core.Planning;

/// <summary>
/// Narrates a deterministic <see cref="CashFlowProjection"/> in prose ("on the 7th leasing leaves,
/// salary lands on the 10th, the tightest point is …"). The AI only explains the engine's output and
/// never produces a number (the Sprint-14 "engine calculates, AI explains" rule); any failure yields a
/// deterministic engine-only summary so the planning page always has something to show.
/// </summary>
public interface ICashFlowExplainer
{
    Task<CashFlowExplanation> ExplainAsync(CashFlowProjection projection, CancellationToken ct);
}
