using System.Text.Json;
using Coffer.Core.Ai;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Security;
using FluentAssertions;
using Xunit.Abstractions;

namespace Coffer.Infrastructure.Tests.AI;

/// <summary>
/// Live check against the real Claude API for each default model — the only way to prove that
/// Anthropic.SDK handles the 5.5 response shapes (adaptive thinking on by default, no temperature).
/// Skips unless the <c>COFFER_LIVE_CLAUDE_KEY</c> environment variable holds an API key, so CI and
/// ordinary test runs never call the network. Costs a fraction of a grosz per run.
/// The key is never printed; prompts and data are synthetic (hard rules #6 / #11).
/// <code>
/// COFFER_LIVE_CLAUDE_KEY=sk-ant-... dotnet test --filter FullyQualifiedName~ClaudeProviderLiveTests
/// </code>
/// </summary>
public class ClaudeProviderLiveTests
{
    private const string KeyVariable = "COFFER_LIVE_CLAUDE_KEY";

    private static readonly AiTool _spendingTool = new(
        "get_monthly_spending",
        "Returns the total spending in PLN for one calendar month.",
        """{"type":"object","properties":{"month":{"type":"string","description":"Month as YYYY-MM"}},"required":["month"]}""");

    private readonly ITestOutputHelper _output;

    public ClaudeProviderLiveTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string> DefaultModels => [AiDefaults.CategorizationModel, AiDefaults.AnalysisModel];

    private sealed record CategoryAnswer(int Id, string Category);

    [SkippableTheory]
    [MemberData(nameof(DefaultModels))]
    public async Task CategorizationStyleJson_ReturnsParsableAnswer(string model)
    {
        var provider = await CreateProviderOrSkipAsync();

        // Same shape as HybridCategorizer: small answer cap, temperature 0 (dropped where rejected).
        var request = new AiRequest
        {
            Provider = AiDefaults.ClaudeProvider,
            Model = model,
            SystemPrompt = "You categorize bank transactions. Answer with JSON only: an array of objects "
                + "{\"id\": number, \"category\": string}, one per transaction, using only the given categories.",
            Prompt = "Categories: Groceries, Fuel, Transport, Subscriptions.\n"
                + "1: SUPERMARKET PURCHASE 54.20 PLN\n"
                + "2: PETROL STATION 210.00 PLN\n"
                + "3: CITY BUS TICKET 4.40 PLN",
            MaxTokens = 512,
            Temperature = 0.0,
        };

        var result = await provider.CompleteJsonAsync<List<CategoryAnswer>>(request, CancellationToken.None);

        _output.WriteLine($"{model}: {result.Usage.InputTokens} in / {result.Usage.OutputTokens} out");
        result.Value.Should().HaveCount(3);
        result.Value.Select(a => a.Id).Should().BeEquivalentTo([1, 2, 3]);
        result.Value.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.Category));
    }

    [SkippableTheory]
    [MemberData(nameof(DefaultModels))]
    public async Task ToolCallingRoundTrip_CallsToolThenAnswers(string model)
    {
        var provider = await CreateProviderOrSkipAsync();
        var conversation = new List<AiChatMessage>
        {
            AiChatMessage.User("How much did I spend in October 2026? Use the tool to look it up."),
        };
        const string SystemPrompt = "You answer questions about the user's finances. Use the provided tool for any "
            + "spending figure; never guess numbers.";

        var first = await provider.CompleteWithToolsAsync(
            new AiToolRequest { Provider = AiDefaults.ClaudeProvider, Model = model, SystemPrompt = SystemPrompt, Messages = conversation, Tools = [_spendingTool] },
            CancellationToken.None);

        _output.WriteLine($"{model} turn 1: {first.Usage.InputTokens} in / {first.Usage.OutputTokens} out");
        var call = first.Value.ToolCalls.Should().ContainSingle().Subject;
        call.ToolName.Should().Be(_spendingTool.Name);
        using (var args = JsonDocument.Parse(call.ArgumentsJson))
        {
            args.RootElement.GetProperty("month").GetString().Should().NotBeNullOrWhiteSpace();
        }

        // Second turn exactly as ChatService builds it: text + tool calls only, no thinking blocks replayed.
        conversation.Add(AiChatMessage.Assistant(first.Value.Text, first.Value.ToolCalls));
        conversation.Add(AiChatMessage.ToolOutputs(
            [new AiToolResult(call.CallId, call.ToolName, """{"month":"2026-10","totalPln":1234.56}""")]));

        var second = await provider.CompleteWithToolsAsync(
            new AiToolRequest { Provider = AiDefaults.ClaudeProvider, Model = model, SystemPrompt = SystemPrompt, Messages = conversation, Tools = [_spendingTool] },
            CancellationToken.None);

        _output.WriteLine($"{model} turn 2: {second.Usage.InputTokens} in / {second.Usage.OutputTokens} out");
        second.Value.IsFinal.Should().BeTrue();
        second.Value.Text.Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<ClaudeProvider> CreateProviderOrSkipAsync()
    {
        var key = Environment.GetEnvironmentVariable(KeyVariable);
        Skip.If(string.IsNullOrWhiteSpace(key), $"{KeyVariable} is not set; skipping the live Claude check.");

        var secrets = new InMemorySecretStore();
        await secrets.SetSecretAsync(AiDefaults.ClaudeApiKeySecret, key!.Trim(), CancellationToken.None);
        return new ClaudeProvider(secrets);
    }
}
