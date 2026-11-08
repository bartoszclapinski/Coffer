using Coffer.Infrastructure.AI;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

public class AiPricingTests
{
    private readonly AiPricing _pricing = new();

    [Fact]
    public void Estimate_KnownModel_PricesInputAndOutput()
    {
        // claude-haiku-4-5 = (1.00 USD/Mtok input, 5.00 USD/Mtok output).
        var cost = _pricing.Estimate("claude-haiku-4-5", inputTokens: 1_000_000, outputTokens: 1_000_000);

        cost.Usd.Should().Be(6.00m);
        cost.Pln.Should().Be(24.00m); // _usdToPln = 4.00
    }

    [Theory]
    [InlineData("claude-haiku-5-5", 0.10, 0.50)]
    [InlineData("claude-sonnet-5-5", 2.00, 10.00)]
    [InlineData("claude-haiku-4-5", 1.00, 5.00)]
    [InlineData("claude-sonnet-4-6", 3.00, 15.00)]
    public void Estimate_EachOfferedModel_UsesItsPublishedRate(string model, double input, double output)
    {
        var inputOnly = _pricing.Estimate(model, inputTokens: 100_000, outputTokens: 0);
        var outputOnly = _pricing.Estimate(model, inputTokens: 0, outputTokens: 1_000_000);

        inputOnly.Usd.Should().Be((decimal)input / 10m);
        outputOnly.Usd.Should().Be((decimal)output);
    }

    [Fact]
    public void Estimate_HaikuFiveFiveLongPrompt_UsesTheLongPromptRateCard()
    {
        // Over 100K prompt tokens Haiku 5.5 bills 0.50 / 2.50 USD per Mtok.
        var cost = _pricing.Estimate("claude-haiku-5-5", inputTokens: 200_000, outputTokens: 1_000_000);

        cost.Usd.Should().Be(0.10m + 2.50m);
    }

    [Fact]
    public void Estimate_UnknownModel_FallsBackToMostExpensiveClaudeRate()
    {
        var cost = _pricing.Estimate("some-future-model", inputTokens: 1_000_000, outputTokens: 1_000_000);

        // Fallback is the Sonnet 4.6 rate (3.00 / 15.00 USD/Mtok) — never under-reports.
        cost.Usd.Should().Be(18.00m);
    }

    [Fact]
    public void Estimate_SubGroszCost_NotRoundedToZero()
    {
        var cost = _pricing.Estimate("claude-haiku-5-5", inputTokens: 100, outputTokens: 50);

        cost.Pln.Should().BeGreaterThan(0m);
    }
}
