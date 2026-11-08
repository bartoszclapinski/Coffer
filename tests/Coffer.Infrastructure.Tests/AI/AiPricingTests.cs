using Coffer.Core.Ai;
using Coffer.Infrastructure.AI;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

public class AiPricingTests
{
    private const string Claude = AiDefaults.ClaudeProvider;

    private readonly AiPricing _pricing = new();

    [Fact]
    public void Estimate_KnownModel_PricesInputAndOutput()
    {
        // claude-haiku-4-5 = (1.00 USD/Mtok input, 5.00 USD/Mtok output).
        var cost = _pricing.Estimate(Claude, "claude-haiku-4-5", inputTokens: 1_000_000, outputTokens: 1_000_000);

        cost.Usd.Should().Be(6.00m);
        cost.Pln.Should().Be(24.00m); // _usdToPln = 4.00
    }

    // Money stays decimal (hard rule #1): the rates are typed decimal, never passed as double.
    public static TheoryData<string, decimal, decimal> PublishedRates => new()
    {
        { "claude-haiku-5-5", 0.10m, 0.50m },
        { "claude-sonnet-5-5", 2.00m, 10.00m },
        { "claude-haiku-4-5", 1.00m, 5.00m },
        { "claude-sonnet-4-6", 3.00m, 15.00m },
    };

    [Theory]
    [MemberData(nameof(PublishedRates))]
    public void Estimate_EachOfferedModel_UsesItsPublishedRate(string model, decimal input, decimal output)
    {
        var inputOnly = _pricing.Estimate(Claude, model, inputTokens: 100_000, outputTokens: 0);
        var outputOnly = _pricing.Estimate(Claude, model, inputTokens: 0, outputTokens: 1_000_000);

        inputOnly.Usd.Should().Be(input / 10m);
        outputOnly.Usd.Should().Be(output);
    }

    [Fact]
    public void EstimateBeforeCall_ModelThatThinksByDefault_AddsTheThinkingHeadroom()
    {
        // Sonnet 5.5 bills thinking as output (10 USD/Mtok): the gate prices the answer plus the
        // full headroom, the most thinking the call can spend.
        var cost = _pricing.EstimateBeforeCall(Claude, "claude-sonnet-5-5", inputTokens: 0, answerTokens: 600);

        cost.Usd.Should().Be((600 + ClaudeModelRules.ThinkingHeadroomTokens) * 10.00m / 1_000_000m);
        cost.Should().Be(_pricing.Estimate(Claude, "claude-sonnet-5-5", 0, 600 + ClaudeModelRules.ThinkingHeadroomTokens));
    }

    [Fact]
    public void EstimateBeforeCall_ModelWithoutDefaultThinking_PricesTheAnswerAlone()
    {
        var cost = _pricing.EstimateBeforeCall(Claude, "claude-haiku-4-5", inputTokens: 1_000, answerTokens: 600);

        cost.Should().Be(_pricing.Estimate(Claude, "claude-haiku-4-5", 1_000, 600));
    }

    [Fact]
    public void EstimateBeforeCall_UnknownModel_AssumesThinkingAndTheFallbackRate()
    {
        var cost = _pricing.EstimateBeforeCall(Claude, "some-future-model", inputTokens: 0, answerTokens: 0);

        cost.Usd.Should().Be(ClaudeModelRules.ThinkingHeadroomTokens * 15.00m / 1_000_000m);
    }

    [Fact]
    public void Estimate_HaikuFiveFiveLongPrompt_UsesTheLongPromptRateCard()
    {
        // Over 100K prompt tokens Haiku 5.5 bills 0.50 / 2.50 USD per Mtok.
        var cost = _pricing.Estimate(Claude, "claude-haiku-5-5", inputTokens: 200_000, outputTokens: 1_000_000);

        cost.Usd.Should().Be(0.10m + 2.50m);
    }

    [Fact]
    public void Estimate_UnknownModel_FallsBackToMostExpensiveClaudeRate()
    {
        var cost = _pricing.Estimate(Claude, "some-future-model", inputTokens: 1_000_000, outputTokens: 1_000_000);

        // Fallback is the Sonnet 4.6 rate (3.00 / 15.00 USD/Mtok) — never under-reports.
        cost.Usd.Should().Be(18.00m);
    }

    [Theory]
    [InlineData("qwen2.5:3b")]
    [InlineData("qwen3.5:latest")]
    [InlineData("claude-sonnet-5-5")]
    public void Estimate_Ollama_IsFreeWhateverTheModel(string model)
    {
        var cost = _pricing.Estimate(AiDefaults.OllamaProvider, model, inputTokens: 1_000_000, outputTokens: 1_000_000);

        cost.Should().Be(new AiCost(0m, 0m), "a local model is never billed, even when its name matches a Claude model");
    }

    [Fact]
    public void EstimateBeforeCall_Ollama_IsFreeWithNoThinkingHeadroom()
    {
        var cost = _pricing.EstimateBeforeCall("ollama", "qwen3.5:latest", inputTokens: 50_000, answerTokens: 4_096);

        cost.Should().Be(new AiCost(0m, 0m), "the provider name matches case-insensitively");
    }

    [Fact]
    public void Estimate_SameModelOnDifferentProviders_PricedPerProvider()
    {
        var onClaude = _pricing.Estimate(Claude, "claude-haiku-5-5", 100_000, 0);
        var onOllama = _pricing.Estimate(AiDefaults.OllamaProvider, "claude-haiku-5-5", 100_000, 0);

        onClaude.Usd.Should().Be(0.01m);
        onOllama.Usd.Should().Be(0m);
    }

    [Fact]
    public void Estimate_UnknownProvider_FallsBackToMostExpensiveClaudeRate()
    {
        var cost = _pricing.Estimate("SomeVendor", "claude-haiku-5-5", inputTokens: 1_000_000, outputTokens: 1_000_000);

        cost.Usd.Should().Be(18.00m, "an unknown provider is never assumed to be cheap");
    }

    [Fact]
    public void Estimate_SubGroszCost_NotRoundedToZero()
    {
        var cost = _pricing.Estimate(Claude, "claude-haiku-5-5", inputTokens: 100, outputTokens: 50);

        cost.Pln.Should().BeGreaterThan(0m);
    }
}
