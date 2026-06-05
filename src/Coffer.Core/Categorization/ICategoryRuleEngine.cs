using Coffer.Core.Domain;

namespace Coffer.Core.Categorization;

/// <summary>
/// Pure, side-effect-free matcher: given a normalised description and the rule set,
/// returns the category of the first enabled rule (by ascending <see cref="Rule.Priority"/>)
/// whose pattern matches, or <c>null</c> if none does. A rule with an invalid regex is
/// skipped, never thrown — bad user input must not break an import.
/// </summary>
public interface ICategoryRuleEngine
{
    Guid? Match(string normalizedDescription, IReadOnlyList<Rule> rules);
}
