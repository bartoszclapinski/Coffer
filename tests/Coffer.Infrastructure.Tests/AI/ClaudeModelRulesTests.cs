using Coffer.Infrastructure.AI;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

public class ClaudeModelRulesTests
{
    [Theory]
    [InlineData("claude-haiku-4-5", true)]
    [InlineData("claude-haiku-4-5-20251001", true)]
    [InlineData("claude-sonnet-4-6", true)]
    [InlineData("claude-opus-4-6", true)]
    [InlineData("claude-opus-4-7", false)]
    [InlineData("claude-opus-4-8", false)]
    [InlineData("claude-sonnet-5", false)]
    [InlineData("claude-haiku-5-5", false)]
    [InlineData("claude-sonnet-5-5", false)]
    [InlineData("claude-opus-5-5", false)]
    [InlineData("claude-fable-5-1", false)]
    [InlineData("claude-haiku-6", false)]
    [InlineData("some-unknown-model", false)]
    [InlineData("", false)]
    public void AcceptsTemperature_OnlyBeforeClaude47(string model, bool expected) =>
        ClaudeModelRules.AcceptsTemperature(model).Should().Be(expected);

    [Theory]
    [InlineData("claude-haiku-4-5", false)]
    [InlineData("claude-sonnet-4-6", false)]
    [InlineData("claude-opus-4-8", false)]
    [InlineData("claude-sonnet-5", true)]
    [InlineData("claude-haiku-5-5", true)]
    [InlineData("claude-sonnet-5-5", true)]
    [InlineData("CLAUDE-HAIKU-5-5", true)]
    [InlineData("claude-haiku-6-1", true)]
    [InlineData("some-unknown-model", true)]
    public void ThinksByDefault_FromTheFiveGenerationOn(string model, bool expected) =>
        ClaudeModelRules.ThinksByDefault(model).Should().Be(expected);

    [Fact]
    public void MaxTokensFor_ThinkingModel_AddsHeadroomOnTopOfTheAnswer() =>
        ClaudeModelRules.MaxTokensFor("claude-haiku-5-5", 512)
            .Should().Be(512 + ClaudeModelRules.ThinkingHeadroomTokens);

    [Fact]
    public void MaxTokensFor_NonThinkingModel_KeepsTheAnswerCap() =>
        ClaudeModelRules.MaxTokensFor("claude-haiku-4-5", 512).Should().Be(512);
}
