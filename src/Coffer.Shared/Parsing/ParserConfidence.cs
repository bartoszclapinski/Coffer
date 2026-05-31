namespace Coffer.Shared.Parsing;

/// <summary>
/// How much the parser trusts its own output. Deterministic parsers return
/// <see cref="High"/>; the future <c>AiAssistedParser</c> fallback will return
/// <see cref="Medium"/> or <see cref="Low"/> depending on JSON validation.
/// Sprint 8 introduces the consumer side (import flow surfacing warnings when
/// confidence is not <c>High</c>).
/// </summary>
/// <remarks>
/// Lives in <c>Coffer.Shared</c> rather than <c>Coffer.Core</c> because it is
/// inseparable from <see cref="ParseResult"/>: every consumer of one consumes
/// the other. Keeping them together avoids a circular Core ↔ Shared reference.
/// </remarks>
public enum ParserConfidence
{
    High = 0,
    Medium = 1,
    Low = 2,
}
