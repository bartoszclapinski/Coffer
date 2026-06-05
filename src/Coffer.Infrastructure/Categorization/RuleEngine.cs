using System.Text.RegularExpressions;
using Coffer.Core.Categorization;
using Coffer.Core.Domain;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Categorization;

/// <summary>
/// Matches a normalised description against the rule set: enabled rules in ascending
/// <see cref="Rule.Priority"/>, case-insensitive, first match wins. A rule whose
/// <see cref="Rule.Pattern"/> is not a valid regex is skipped and logged once per call —
/// a malformed user rule must never throw into an import.
/// </summary>
public sealed class RuleEngine : ICategoryRuleEngine
{
    private static readonly TimeSpan _matchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly ILogger<RuleEngine> _logger;

    public RuleEngine(ILogger<RuleEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Guid? Match(string normalizedDescription, IReadOnlyList<Rule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (string.IsNullOrEmpty(normalizedDescription))
        {
            return null;
        }

        foreach (var rule in rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority))
        {
            bool matched;
            try
            {
                matched = Regex.IsMatch(
                    normalizedDescription,
                    rule.Pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    _matchTimeout);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(
                    ex, "Skipping rule {RuleId} — invalid regex pattern", rule.Id);
                continue;
            }
            catch (RegexMatchTimeoutException ex)
            {
                _logger.LogWarning(
                    ex, "Skipping rule {RuleId} — regex match timed out", rule.Id);
                continue;
            }

            if (matched)
            {
                return rule.CategoryId;
            }
        }

        return null;
    }
}
