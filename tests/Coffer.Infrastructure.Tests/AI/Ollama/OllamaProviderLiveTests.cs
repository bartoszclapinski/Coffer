using System.Text.Json;
using Coffer.Core.Ai;
using Coffer.Infrastructure.AI.Ollama;
using FluentAssertions;
using Xunit.Abstractions;

namespace Coffer.Infrastructure.Tests.AI.Ollama;

/// <summary>
/// Live check against a real local Ollama: one categorisation-style JSON completion and one tool-calling
/// round trip through <see cref="OllamaProvider"/>. Skips unless <c>COFFER_LIVE_OLLAMA=1</c>, so CI and
/// ordinary runs never touch the network. Uses <c>qwen2.5:3b</c> (fast) unless
/// <c>COFFER_LIVE_OLLAMA_MODEL</c> names another pulled model, and <c>http://localhost:11434</c> unless
/// <c>COFFER_LIVE_OLLAMA_ENDPOINT</c> is set. Prompts and data are synthetic.
/// <code>
/// COFFER_LIVE_OLLAMA=1 dotnet test --filter FullyQualifiedName~OllamaProviderLiveTests
/// </code>
/// </summary>
public class OllamaProviderLiveTests
{
    private const string EnabledVariable = "COFFER_LIVE_OLLAMA";
    private const string ModelVariable = "COFFER_LIVE_OLLAMA_MODEL";
    private const string EndpointVariable = "COFFER_LIVE_OLLAMA_ENDPOINT";
    private const string DefaultModel = "qwen2.5:3b";

    private static readonly AiTool _spendingTool = new(
        "get_monthly_spending",
        "Returns the total spending in PLN for one calendar month.",
        """{"type":"object","properties":{"month":{"type":"string","description":"Month as YYYY-MM"}},"required":["month"]}""");

    private readonly ITestOutputHelper _output;

    public OllamaProviderLiveTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task CategorizationStyleJson_ReturnsAnIndexArray()
    {
        var (provider, model) = CreateProviderOrSkip();

        // Same shape as HybridCategorizer: an array of 0-based indexes, temperature 0, small answer cap.
        var request = new AiRequest
        {
            Provider = AiDefaults.OllamaProvider,
            Model = model,
            SystemPrompt = "You are a Polish personal finance categorizer. Assign each transaction to exactly one "
                + "category from the supplied list. Respond with ONLY a JSON array of 0-based integer indexes into "
                + "the categories array, in the same order as the transactions. No prose.",
            Prompt = "Categories: [\"Groceries\", \"Fuel\", \"Transport\", \"Rent\"]\n\n"
                + "Transactions:\n1. \"SUPERMARKET PURCHASE\"\n2. \"PETROL STATION\"\n3. \"CITY BUS TICKET\"\n\n"
                + "Return ONLY a JSON array of 3 integers (0-based indexes into the categories array), in the same "
                + "order as the transactions, e.g.: [0, 4, 3].",
            MaxTokens = 512,
            Temperature = 0.0,
        };

        var result = await provider.CompleteJsonAsync<int[]>(request, CancellationToken.None);

        _output.WriteLine($"{model}: [{string.Join(", ", result.Value)}], {result.Usage.InputTokens} in / {result.Usage.OutputTokens} out");
        result.Value.Should().HaveCount(3).And.OnlyContain(i => i >= 0 && i < 4);
        result.Usage.Provider.Should().Be(AiDefaults.OllamaProvider);
        result.Usage.InputTokens.Should().BePositive();
    }

    [SkippableFact]
    public async Task ToolCallingRoundTrip_CallsToolThenAnswers()
    {
        var (provider, model) = CreateProviderOrSkip();
        const string SystemPrompt = "You answer questions about the user's finances. Use the provided tool for any "
            + "spending figure; never guess numbers.";
        var conversation = new List<AiChatMessage>
        {
            AiChatMessage.User("How much did I spend in October 2026? Use the tool to look it up."),
        };

        var first = await provider.CompleteWithToolsAsync(
            new AiToolRequest { Provider = AiDefaults.OllamaProvider, Model = model, SystemPrompt = SystemPrompt, Messages = conversation, Tools = [_spendingTool] },
            CancellationToken.None);

        _output.WriteLine($"{model} turn 1: {first.Usage.InputTokens} in / {first.Usage.OutputTokens} out");
        var call = first.Value.ToolCalls.Should().ContainSingle().Subject;
        call.ToolName.Should().Be(_spendingTool.Name);
        call.CallId.Should().NotBeNullOrWhiteSpace();
        using (var args = JsonDocument.Parse(call.ArgumentsJson))
        {
            args.RootElement.GetProperty("month").GetString().Should().NotBeNullOrWhiteSpace();
        }

        // Second turn exactly as ChatService builds it.
        conversation.Add(AiChatMessage.Assistant(first.Value.Text, first.Value.ToolCalls));
        conversation.Add(AiChatMessage.ToolOutputs(
            [new AiToolResult(call.CallId, call.ToolName, """{"month":"2026-10","totalPln":1234.56}""")]));

        var second = await provider.CompleteWithToolsAsync(
            new AiToolRequest { Provider = AiDefaults.OllamaProvider, Model = model, SystemPrompt = SystemPrompt, Messages = conversation, Tools = [_spendingTool] },
            CancellationToken.None);

        _output.WriteLine($"{model} turn 2: {second.Usage.InputTokens} in / {second.Usage.OutputTokens} out");
        second.Value.IsFinal.Should().BeTrue();
        second.Value.Text.Should().NotBeNullOrWhiteSpace();
    }

    private static (OllamaProvider Provider, string Model) CreateProviderOrSkip()
    {
        Skip.IfNot(
            Environment.GetEnvironmentVariable(EnabledVariable) == "1",
            $"{EnabledVariable} is not 1; skipping the live Ollama check.");

        var settings = new StubAiSettings();
        var endpointText = Environment.GetEnvironmentVariable(EndpointVariable);
        if (!string.IsNullOrWhiteSpace(endpointText))
        {
            OllamaEndpoint.TryParse(endpointText, out var endpoint).Should().BeTrue($"{EndpointVariable} must be an http(s) URL");
            settings.OllamaEndpoint = endpoint!;
        }

        var model = Environment.GetEnvironmentVariable(ModelVariable);
        return (new OllamaProvider(settings, new OllamaApiClient()), string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim());
    }
}
