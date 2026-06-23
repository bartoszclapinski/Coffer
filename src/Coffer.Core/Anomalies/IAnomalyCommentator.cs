namespace Coffer.Core.Anomalies;

/// <summary>
/// Replaces the deterministic 13-A templated <see cref="AnomalyCandidate.Title"/> /
/// <see cref="AnomalyCandidate.Description"/> of the highest-ranked candidates with LLM-written
/// Polish prose (doc 04, "statistics detect, AI explains"). Implementations are budget-gated and
/// metered; on any failure — over-budget, offline, malformed response — they must return the input
/// candidates unchanged so the templated text always survives (graceful fallback).
/// </summary>
public interface IAnomalyCommentator
{
    /// <summary>
    /// Returns a list of the same length and order as <paramref name="candidates"/>; each element is
    /// either the original (fallback) or a copy with LLM-written title/description.
    /// </summary>
    Task<IReadOnlyList<AnomalyCandidate>> CommentAsync(
        IReadOnlyList<AnomalyCandidate> candidates,
        CancellationToken ct);
}
