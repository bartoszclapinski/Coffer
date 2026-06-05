using Coffer.Core.Domain;
using Coffer.Infrastructure.Categorization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Categorization;

public class RuleEngineTests
{
    private static readonly RuleEngine _engine = new(NullLogger<RuleEngine>.Instance);

    private static Rule Rule(string pattern, int priority, Guid categoryId, bool enabled = true) => new()
    {
        Id = Guid.NewGuid(),
        Pattern = pattern,
        Priority = priority,
        CategoryId = categoryId,
        IsEnabled = enabled,
    };

    [Fact]
    public void Match_LowestPriorityWins_WhenSeveralPatternsMatch()
    {
        var groceries = Guid.NewGuid();
        var subscriptions = Guid.NewGuid();
        var rules = new[]
        {
            Rule("LIDL", priority: 20, groceries),
            Rule("LIDL", priority: 10, subscriptions),
        };

        _engine.Match("PLATNOSC KARTA LIDL WARSZAWA", rules)
            .Should().Be(subscriptions, "priority 10 outranks 20 even though both patterns match");
    }

    [Fact]
    public void Match_SkipsDisabledRules()
    {
        var fuel = Guid.NewGuid();
        var rules = new[] { Rule("ORLEN", priority: 10, fuel, enabled: false) };

        _engine.Match("ORLEN STACJA PALIW", rules).Should().BeNull();
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var fuel = Guid.NewGuid();
        var rules = new[] { Rule("orlen", priority: 10, fuel) };

        _engine.Match("ORLEN STACJA", rules).Should().Be(fuel);
    }

    [Fact]
    public void Match_InvalidRegex_IsSkippedAndDoesNotThrow()
    {
        var groceries = Guid.NewGuid();
        var rules = new[]
        {
            Rule("(unclosed", priority: 10, Guid.NewGuid()),
            Rule("LIDL", priority: 20, groceries),
        };

        _engine.Match("LIDL SP Z OO", rules)
            .Should().Be(groceries, "a malformed pattern is skipped so a later valid rule can match");
    }

    [Fact]
    public void Match_NoRuleMatches_ReturnsNull()
    {
        var rules = new[] { Rule("ORLEN", priority: 10, Guid.NewGuid()) };

        _engine.Match("UNKNOWN MERCHANT", rules).Should().BeNull();
    }

    [Fact]
    public void Match_EmptyDescription_ReturnsNull()
    {
        var rules = new[] { Rule(".", priority: 10, Guid.NewGuid()) };

        _engine.Match("", rules).Should().BeNull();
    }
}
