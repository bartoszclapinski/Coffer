using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Coffer.Core.Ai;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

/// <summary>
/// The request body <see cref="ClaudeProvider"/> sends through Anthropic.SDK, captured by a fake
/// <see cref="HttpMessageHandler"/>: temperature only where the model accepts it, thinking headroom
/// on <c>max_tokens</c>, no forced <c>tool_choice</c>, no assistant prefill, and answers read past
/// any leading thinking block.
/// </summary>
public class ClaudeProviderRequestTests
{
    private const string FakeKey = "sk-ant-test-not-a-real-key";

    private sealed record Categorized(string Category);

    [Fact]
    public async Task CompleteJson_HaikuFiveFive_OmitsTemperatureAndAddsThinkingHeadroom()
    {
        var handler = new CapturingHandler(TextResponse("{\"category\":\"Groceries\"}"));
        var provider = await CreateAsync(handler);

        var result = await provider.CompleteJsonAsync<Categorized>(
            new AiRequest { Prompt = "p", Provider = AiDefaults.ClaudeProvider, Model = "claude-haiku-5-5", MaxTokens = 512, Temperature = 0.0 },
            CancellationToken.None);

        result.Value.Category.Should().Be("Groceries");
        var body = handler.SingleBody();
        body.ContainsKey("temperature").Should().BeFalse();
        body["max_tokens"]!.GetValue<int>().Should().Be(512 + ClaudeModelRules.ThinkingHeadroomTokens);
        body["model"]!.GetValue<string>().Should().Be("claude-haiku-5-5");
    }

    [Fact]
    public async Task CompleteJson_HaikuFourFive_KeepsTemperatureAndTheAnswerCap()
    {
        var handler = new CapturingHandler(TextResponse("{\"category\":\"Fuel\"}"));
        var provider = await CreateAsync(handler);

        await provider.CompleteJsonAsync<Categorized>(
            new AiRequest { Prompt = "p", Provider = AiDefaults.ClaudeProvider, Model = "claude-haiku-4-5", MaxTokens = 512, Temperature = 0.0 },
            CancellationToken.None);

        var body = handler.SingleBody();
        body["temperature"]!.GetValue<double>().Should().Be(0.0);
        body["max_tokens"]!.GetValue<int>().Should().Be(512);
    }

    [Fact]
    public async Task CompleteJson_ResponseStartsWithThinkingBlock_ReadsTheTextAnswer()
    {
        var handler = new CapturingHandler(TextResponse("[{\"category\":\"Rent\"}]", withThinking: true));
        var provider = await CreateAsync(handler);

        var result = await provider.CompleteJsonAsync<List<Categorized>>(
            new AiRequest { Prompt = "p", Provider = AiDefaults.ClaudeProvider, Model = "claude-haiku-5-5" },
            CancellationToken.None);

        result.Value.Should().ContainSingle().Which.Category.Should().Be("Rent");
        result.Usage.Should().Be(new AiUsage(AiDefaults.ClaudeProvider, "claude-haiku-5-5", 11, 22));
    }

    [Fact]
    public async Task CompleteWithTools_SonnetFiveFive_SendsAutoToolsWithoutTemperatureOrPrefill()
    {
        var handler = new CapturingHandler(ToolUseResponse());
        var provider = await CreateAsync(handler);

        var turn = await provider.CompleteWithToolsAsync(
            new AiToolRequest
            {
                Provider = AiDefaults.ClaudeProvider,
                Model = "claude-sonnet-5-5",
                Messages = [AiChatMessage.User("How much did I spend?")],
                Tools = [_spendingTool],
                MaxTokens = 1024,
            },
            CancellationToken.None);

        turn.Value.ToolCalls.Should().ContainSingle()
            .Which.Should().Be(new AiToolCall("toolu_1", "get_spending", "{\"month\":\"2026-10\"}"));

        var body = handler.SingleBody();
        body.ContainsKey("temperature").Should().BeFalse();
        body["max_tokens"]!.GetValue<int>().Should().Be(1024 + ClaudeModelRules.ThinkingHeadroomTokens);
        body["tools"]!.AsArray().Should().ContainSingle();
        if (body["tool_choice"] is JsonObject toolChoice)
        {
            toolChoice["type"]!.GetValue<string>().Should().Be("auto");
        }

        body["messages"]!.AsArray().Last()!["role"]!.GetValue<string>().Should().Be("user");
    }

    [Fact]
    public async Task CompleteWithTools_ToolResultTurn_EndsWithUserTurnAndReplaysNoThinking()
    {
        var handler = new CapturingHandler(TextResponse("You spent 100 PLN."));
        var provider = await CreateAsync(handler);
        var call = new AiToolCall("toolu_1", "get_spending", "{\"month\":\"2026-10\"}");

        var turn = await provider.CompleteWithToolsAsync(
            new AiToolRequest
            {
                Provider = AiDefaults.ClaudeProvider,
                Model = "claude-sonnet-5-5",
                Messages =
                [
                    AiChatMessage.User("How much did I spend?"),
                    AiChatMessage.Assistant(null, [call]),
                    AiChatMessage.ToolOutputs([new AiToolResult("toolu_1", "get_spending", "{\"total\":100}")]),
                ],
                Tools = [_spendingTool],
            },
            CancellationToken.None);

        turn.Value.IsFinal.Should().BeTrue();
        var messages = handler.SingleBody()["messages"]!.AsArray();
        messages.Last()!["role"]!.GetValue<string>().Should().Be("user");
        var raw = messages.ToJsonString();
        raw.Should().NotContain("\"thinking\"").And.NotContain("redacted_thinking");
    }

    [Fact]
    public async Task Complete_ApiKeyNeverAppearsInTheRequestBody()
    {
        var handler = new CapturingHandler(TextResponse("ok"));
        var provider = await CreateAsync(handler);

        await provider.CompleteAsync(new AiRequest { Prompt = "p", Provider = AiDefaults.ClaudeProvider, Model = "claude-haiku-5-5" }, CancellationToken.None);

        handler.Bodies.Should().ContainSingle().Which.Should().NotContain(FakeKey);
    }

    private static readonly AiTool _spendingTool = new(
        "get_spending",
        "Total spending for a month.",
        """{"type":"object","properties":{"month":{"type":"string"}},"required":["month"]}""");

    private static async Task<ClaudeProvider> CreateAsync(HttpMessageHandler handler)
    {
        var secrets = new InMemorySecretStore();
        await secrets.SetSecretAsync(AiDefaults.ClaudeApiKeySecret, FakeKey, CancellationToken.None);
        return new ClaudeProvider(secrets, handler);
    }

    private static string TextResponse(string text, bool withThinking = false)
    {
        var content = new JsonArray();
        if (withThinking)
        {
            // Display "omitted" (the 5.5 default): empty thinking text, signature only.
            content.Add(new JsonObject { ["type"] = "thinking", ["thinking"] = "", ["signature"] = "sig" });
        }

        content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
        return Envelope(content, "end_turn");
    }

    private static string ToolUseResponse()
    {
        var content = new JsonArray
        {
            new JsonObject { ["type"] = "thinking", ["thinking"] = "", ["signature"] = "sig" },
            new JsonObject
            {
                ["type"] = "tool_use",
                ["id"] = "toolu_1",
                ["name"] = "get_spending",
                ["input"] = new JsonObject { ["month"] = "2026-10" },
            },
        };
        return Envelope(content, "tool_use");
    }

    private static string Envelope(JsonArray content, string stopReason) =>
        new JsonObject
        {
            ["id"] = "msg_1",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = "test",
            ["content"] = content,
            ["stop_reason"] = stopReason,
            ["usage"] = new JsonObject { ["input_tokens"] = 11, ["output_tokens"] = 22 },
        }.ToJsonString();

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        public JsonObject SingleBody() =>
            JsonNode.Parse(Bodies.Should().ContainSingle().Subject)!.AsObject();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }
}
