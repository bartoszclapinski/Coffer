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

    [Fact]
    public void Estimate_UnknownModel_FallsBackToSonnetRate()
    {
        var cost = _pricing.Estimate("some-future-model", inputTokens: 1_000_000, outputTokens: 0);

        // Fallback input rate is 3.00 USD/Mtok — never under-reports.
        cost.Usd.Should().Be(3.00m);
    }

    [Fact]
    public void Estimate_SubGroszCost_NotRoundedToZero()
    {
        var cost = _pricing.Estimate("claude-haiku-4-5", inputTokens: 100, outputTokens: 50);

        cost.Pln.Should().BeGreaterThan(0m);
    }
}
