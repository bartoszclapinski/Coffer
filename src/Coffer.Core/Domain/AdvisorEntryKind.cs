namespace Coffer.Core.Domain;

/// <summary>
/// Discriminates the two kinds of line the advisor's LLM produces for a day's
/// <see cref="AdvisorReport"/> (doc 07): a per-goal <see cref="Risk"/> sentence, or a global
/// cutting <see cref="Suggestion"/> grounded in a spending category.
/// </summary>
public enum AdvisorEntryKind
{
    Risk,
    Suggestion,
}
