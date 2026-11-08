using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coffer.Core.Ai;
using Coffer.Infrastructure.AI.Ollama;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI.Ollama;

/// <summary>
/// <see cref="OllamaProvider"/> over a fake <see cref="HttpMessageHandler"/>: the <c>/api/chat</c> request
/// shape (model, no streaming, context window, max tokens, temperature, JSON schema format, thinking off for
/// JSON), answers read from <c>content</c> only, tool calls in both directions, usage, and every failure
/// mapped to <see cref="AiProviderUnavailableException"/> without prompt text.
/// </summary>
public class OllamaProviderTests
{
    private const string Model = "qwen2.5:3b";
    private const string SecretPrompt = "Transakcja: SKLEP ABC 54,20 PLN";

    private static readonly AiTool _spendingTool = new(
        "get_spending",
        "Total spending for a month.",
        """{"type":"object","properties":{"month":{"type":"string"}},"required":["month"]}""");

    private sealed record Categorized(string? Category, decimal? Amount);

    [Fact]
    public async Task Complete_SendsNonStreamingChatWithContextWindowMaxTokensAndTemperature()
    {
        var handler = new FakeOllamaHandler().Respond(FakeOllamaHandler.TextResponse("Hello."));
        var provider = Provider(handler);

        var result = await provider.CompleteAsync(
            new AiRequest
            {
                Provider = AiDefaults.OllamaProvider,
                Model = Model,
                SystemPrompt = "system text",
                Prompt = "user text",
                MaxTokens = 700,
                Temperature = 0.4,
            },
            CancellationToken.None);

        result.Value.Should().Be("Hello.");
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("http://localhost:11434/api/chat"));

        var body = handler.SingleBody();
        body["model"]!.GetValue<string>().Should().Be(Model);
        body["stream"]!.GetValue<bool>().Should().BeFalse();
        body["options"]!["num_ctx"]!.GetValue<int>().Should().Be(16384);
        body["options"]!["num_predict"]!.GetValue<int>().Should().Be(700);
        body["options"]!["temperature"]!.GetValue<double>().Should().Be(0.4);
        body.ContainsKey("format").Should().BeFalse();
        body.ContainsKey("think").Should().BeFalse("text completions keep the model's own thinking default");
        body.ContainsKey("tools").Should().BeFalse();

        var messages = body["messages"]!.AsArray();
        messages.Should().HaveCount(2);
        messages[0]!["role"]!.GetValue<string>().Should().Be("system");
        messages[0]!["content"]!.GetValue<string>().Should().Be("system text");
        messages[1]!["role"]!.GetValue<string>().Should().Be("user");
        messages[1]!["content"]!.GetValue<string>().Should().Be("user text");
    }

    [Fact]
    public async Task Complete_UsesTheEndpointFromSettingsOnEachCall()
    {
        var handler = new FakeOllamaHandler()
            .Respond(FakeOllamaHandler.TextResponse("a"))
            .Respond(FakeOllamaHandler.TextResponse("b"));
        var settings = new StubAiSettings { OllamaEndpoint = new Uri("http://192.168.1.20:11434/ollama") };
        var provider = new OllamaProvider(settings, new OllamaApiClient(handler));

        await provider.CompleteAsync(Request(), CancellationToken.None);
        settings.OllamaEndpoint = new Uri("http://127.0.0.1:9999");
        await provider.CompleteAsync(Request(), CancellationToken.None);

        handler.Requests.Select(r => r.Uri).Should().Equal(
            new Uri("http://192.168.1.20:11434/ollama/api/chat"),
            new Uri("http://127.0.0.1:9999/api/chat"));
    }

    [Fact]
    public async Task CompleteJson_ArrayResult_SendsArraySchemaAndThinkFalse()
    {
        var handler = new FakeOllamaHandler().Respond(FakeOllamaHandler.TextResponse("[0, 1, 3]"));
        var provider = Provider(handler);

        var result = await provider.CompleteJsonAsync<int[]>(Request(maxTokens: 512, temperature: 0.0), CancellationToken.None);

        result.Value.Should().Equal(0, 1, 3);
        var body = handler.SingleBody();
        body["think"]!.GetValue<bool>().Should().BeFalse();
        body["options"]!["num_predict"]!.GetValue<int>().Should().Be(512);
        body["options"]!["temperature"]!.GetValue<double>().Should().Be(0.0);

        // A schema, not "json": format "json" makes Ollama emit an object, which breaks an array answer.
        var format = body["format"]!.AsObject();
        format["type"]!.GetValue<string>().Should().Be("array");
        format["items"]!["type"]!.GetValue<string>().Should().Be("integer");
    }

    [Fact]
    public async Task CompleteJson_ObjectResult_SchemaRequiresEveryPropertyAndKeepsNumbersNumeric()
    {
        var handler = new FakeOllamaHandler().Respond(
            FakeOllamaHandler.TextResponse("""{"category":"Groceries","amount":-54.20}"""));
        var provider = Provider(handler);

        var result = await provider.CompleteJsonAsync<Categorized>(Request(), CancellationToken.None);

        result.Value.Should().Be(new Categorized("Groceries", -54.20m));
        var format = handler.SingleBody()["format"]!.AsObject();
        format["type"]!.GetValue<string>().Should().Be("object", "the root answer may not be null");
        format["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().BeEquivalentTo("category", "amount");
        format["properties"]!["amount"]!["type"]!.ToJsonString().Should().Contain("number").And.NotContain("string");
    }

    [Fact]
    public async Task CompleteJson_ReadsTheAnswerFromContentNeverFromThinking()
    {
        var handler = new FakeOllamaHandler().Respond(
            FakeOllamaHandler.TextResponse("Here you go:\n```json\n[2, 1]\n```", thinking: "[9, 9] maybe"));
        var provider = Provider(handler);

        var result = await provider.CompleteJsonAsync<int[]>(Request(), CancellationToken.None);

        result.Value.Should().Equal(2, 1);
    }

    [Fact]
    public async Task CompleteJson_EmptyContentWithOnlyThinking_ThrowsInsteadOfReadingThinking()
    {
        var handler = new FakeOllamaHandler().Respond(FakeOllamaHandler.TextResponse("", thinking: "[1, 2]"));
        var provider = Provider(handler);

        var act = () => provider.CompleteJsonAsync<int[]>(Request(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CompleteJson_ReportsUsageFromEvalCountsAsOllama()
    {
        var handler = new FakeOllamaHandler().Respond(FakeOllamaHandler.ChatResponse(
            new JsonObject { ["role"] = "assistant", ["content"] = "[1]" }, promptTokens: 321, evalTokens: 12));
        var provider = Provider(handler);

        var result = await provider.CompleteJsonAsync<int[]>(Request(), CancellationToken.None);

        result.Usage.Should().Be(new AiUsage(AiDefaults.OllamaProvider, Model, 321, 12));
    }

    [Fact]
    public async Task Complete_MissingEvalCounts_ReportZeroUsage()
    {
        // A fully cached prompt can come back without prompt_eval_count.
        var handler = new FakeOllamaHandler().Respond(
            """{"model":"qwen2.5:3b","message":{"role":"assistant","content":"ok"},"done":true}""");
        var provider = Provider(handler);

        var result = await provider.CompleteAsync(Request(), CancellationToken.None);

        result.Usage.Should().Be(new AiUsage(AiDefaults.OllamaProvider, Model, 0, 0));
    }

    [Fact]
    public async Task CompleteWithTools_MapsToolsHistoryCallsAndResultsToOllamaMessages()
    {
        var handler = new FakeOllamaHandler().Respond(FakeOllamaHandler.TextResponse("You spent 100 PLN."));
        var provider = Provider(handler);
        var call = new AiToolCall("call_1", "get_spending", """{"month":"2026-10"}""");

        var result = await provider.CompleteWithToolsAsync(
            new AiToolRequest
            {
                Provider = AiDefaults.OllamaProvider,
                Model = Model,
                SystemPrompt = "system text",
                Messages =
                [
                    AiChatMessage.User("How much did I spend?"),
                    AiChatMessage.Assistant("Checking.", [call]),
                    AiChatMessage.ToolOutputs([new AiToolResult("call_1", "get_spending", """{"total":100}""")]),
                ],
                Tools = [_spendingTool],
                MaxTokens = 1024,
                Temperature = 0.3,
            },
            CancellationToken.None);

        result.Value.IsFinal.Should().BeTrue();
        result.Value.Text.Should().Be("You spent 100 PLN.");

        var body = handler.SingleBody();
        body.ContainsKey("format").Should().BeFalse();
        body["options"]!["num_ctx"]!.GetValue<int>().Should().Be(16384);
        body["options"]!["num_predict"]!.GetValue<int>().Should().Be(1024);

        var tool = body["tools"]!.AsArray().Should().ContainSingle().Subject!;
        tool["type"]!.GetValue<string>().Should().Be("function");
        tool["function"]!["name"]!.GetValue<string>().Should().Be("get_spending");
        tool["function"]!["description"]!.GetValue<string>().Should().Be("Total spending for a month.");
        tool["function"]!["parameters"]!["required"]![0]!.GetValue<string>().Should().Be("month");

        var messages = body["messages"]!.AsArray();
        messages.Select(m => m!["role"]!.GetValue<string>()).Should().Equal("system", "user", "assistant", "tool");

        var assistant = messages[2]!;
        assistant["content"]!.GetValue<string>().Should().Be("Checking.");
        var sentCall = assistant["tool_calls"]!.AsArray().Should().ContainSingle().Subject!;
        sentCall["id"]!.GetValue<string>().Should().Be("call_1");
        sentCall["function"]!["name"]!.GetValue<string>().Should().Be("get_spending");
        sentCall["function"]!["arguments"]!.GetValueKind().Should().Be(JsonValueKind.Object, "Ollama takes arguments as an object");
        sentCall["function"]!["arguments"]!["month"]!.GetValue<string>().Should().Be("2026-10");

        var toolMessage = messages[3]!;
        toolMessage["content"]!.GetValue<string>().Should().Be("""{"total":100}""");
        toolMessage["tool_name"]!.GetValue<string>().Should().Be("get_spending");
        toolMessage["tool_call_id"]!.GetValue<string>().Should().Be("call_1");
    }

    [Fact]
    public async Task CompleteWithTools_SeveralToolResults_BecomeOneToolMessageEach()
    {
        var handler = new FakeOllamaHandler().Respond(FakeOllamaHandler.TextResponse("Done."));
        var provider = Provider(handler);

        await provider.CompleteWithToolsAsync(
            ToolRequest(
                AiChatMessage.User("q"),
                AiChatMessage.Assistant(null, [new AiToolCall("a", "t1", "{}"), new AiToolCall("b", "t2", "")]),
                AiChatMessage.ToolOutputs([new AiToolResult("a", "t1", "{}"), new AiToolResult("b", "t2", "[]")])),
            CancellationToken.None);

        var messages = handler.SingleBody()["messages"]!.AsArray();
        messages.Select(m => m!["role"]!.GetValue<string>()).Should().Equal("user", "assistant", "tool", "tool");
        messages[1]!["content"]!.GetValue<string>().Should().BeEmpty();
        messages[1]!["tool_calls"]![1]!["function"]!["arguments"]!.ToJsonString().Should().Be("{}");
        messages[3]!["tool_name"]!.GetValue<string>().Should().Be("t2");
    }

    [Fact]
    public async Task CompleteWithTools_ResponseToolCalls_BecomeAiToolCallsWithJsonArguments()
    {
        var message = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = "",
            ["tool_calls"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "call_s4eqlp54",
                    ["function"] = new JsonObject
                    {
                        ["index"] = 0,
                        ["name"] = "get_spending",
                        ["arguments"] = new JsonObject { ["month"] = "2026-10" },
                    },
                },
            },
        };
        var handler = new FakeOllamaHandler().Respond(FakeOllamaHandler.ChatResponse(message, 187, 29));
        var provider = Provider(handler);

        var result = await provider.CompleteWithToolsAsync(ToolRequest(AiChatMessage.User("q")), CancellationToken.None);

        result.Value.IsFinal.Should().BeFalse();
        result.Value.Text.Should().BeNull("empty content is no text");
        var call = result.Value.ToolCalls.Should().ContainSingle().Subject;
        call.CallId.Should().Be("call_s4eqlp54");
        call.ToolName.Should().Be("get_spending");
        JsonNode.Parse(call.ArgumentsJson)!["month"]!.GetValue<string>().Should().Be("2026-10");
        result.Usage.Should().Be(new AiUsage(AiDefaults.OllamaProvider, Model, 187, 29));
    }

    [Fact]
    public async Task CompleteWithTools_ToolCallsWithoutIds_GetUniqueGeneratedIds()
    {
        var message = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = "",
            ["tool_calls"] = new JsonArray
            {
                new JsonObject { ["function"] = new JsonObject { ["name"] = "t1", ["arguments"] = new JsonObject() } },
                new JsonObject { ["function"] = new JsonObject { ["name"] = "t2", ["arguments"] = """{"x":1}""" } },
                new JsonObject { ["function"] = new JsonObject { ["name"] = "t3" } },
            },
        };
        var handler = new FakeOllamaHandler().Respond(FakeOllamaHandler.ChatResponse(message));
        var provider = Provider(handler);

        var result = await provider.CompleteWithToolsAsync(ToolRequest(AiChatMessage.User("q")), CancellationToken.None);

        var calls = result.Value.ToolCalls;
        calls.Select(c => c.ToolName).Should().Equal("t1", "t2", "t3");
        calls.Should().OnlyContain(c => c.CallId.StartsWith("call_", StringComparison.Ordinal));
        calls.Select(c => c.CallId).Should().OnlyHaveUniqueItems();
        calls[0].ArgumentsJson.Should().Be("{}");
        calls[1].ArgumentsJson.Should().Be("""{"x":1}""", "arguments sent as a JSON string are tolerated");
        calls[2].ArgumentsJson.Should().Be("{}");
    }

    [Fact]
    public async Task CompleteWithTools_NeitherTextNorToolCalls_Throws()
    {
        var handler = new FakeOllamaHandler().Respond(FakeOllamaHandler.TextResponse("", thinking: "still thinking"));
        var provider = Provider(handler);

        var act = () => provider.CompleteWithToolsAsync(ToolRequest(AiChatMessage.User("q")), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ConnectionRefused_ThrowsUnavailableNamingEndpointAndModelButNotThePrompt()
    {
        var handler = new FakeOllamaHandler().Throw(new HttpRequestException("No connection could be made (localhost:11434)"));
        var provider = Provider(handler);

        var act = () => provider.CompleteJsonAsync<int[]>(Request(), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<AiProviderUnavailableException>()).Which;
        ex.Reason.Should().Be(AiProviderUnavailableReason.Unreachable);
        ex.Provider.Should().Be(AiDefaults.OllamaProvider);
        ex.Endpoint.Should().Be("http://localhost:11434");
        ex.Model.Should().Be(Model);
        ex.Message.Should().Contain("http://localhost:11434").And.Contain(Model).And.NotContain(SecretPrompt);
    }

    [Fact]
    public async Task ModelNotFound_ThrowsUnavailableWithModelNotFound()
    {
        var handler = new FakeOllamaHandler().Respond("""{"error":"model 'qwen2.5:3b' not found"}""", HttpStatusCode.NotFound);
        var provider = Provider(handler);

        var act = () => provider.CompleteAsync(Request(), CancellationToken.None);

        (await act.Should().ThrowAsync<AiProviderUnavailableException>())
            .Which.Reason.Should().Be(AiProviderUnavailableReason.ModelNotFound);
    }

    [Fact]
    public async Task ModelWithoutTools_ThrowsUnavailableWithoutCopyingTheServerText()
    {
        var handler = new FakeOllamaHandler().Respond(
            """{"error":"registry.ollama.ai/library/tiny:1b does not support tools; prompt was: Transakcja"}""",
            HttpStatusCode.BadRequest);
        var provider = Provider(handler);

        var act = () => provider.CompleteWithToolsAsync(ToolRequest(AiChatMessage.User(SecretPrompt)), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<AiProviderUnavailableException>()).Which;
        ex.Reason.Should().Be(AiProviderUnavailableReason.ModelUnsupported);
        ex.Message.Should().NotContain("Transakcja").And.NotContain("registry.ollama.ai");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task OtherNonSuccess_ThrowsUnavailableRejected(HttpStatusCode status)
    {
        var handler = new FakeOllamaHandler().Respond("""{"error":"boom"}""", status);
        var provider = Provider(handler);

        var act = () => provider.CompleteAsync(Request(), CancellationToken.None);

        (await act.Should().ThrowAsync<AiProviderUnavailableException>())
            .Which.Reason.Should().Be(AiProviderUnavailableReason.Rejected);
    }

    [Fact]
    public async Task SuccessWithNonJsonBody_ThrowsUnavailableRejected()
    {
        var handler = new FakeOllamaHandler().Respond("<html>proxy error</html>");
        var provider = Provider(handler);

        var act = () => provider.CompleteAsync(Request(), CancellationToken.None);

        (await act.Should().ThrowAsync<AiProviderUnavailableException>())
            .Which.Reason.Should().Be(AiProviderUnavailableReason.Rejected);
    }

    [Fact]
    public async Task NoAnswerWithinTheTimeout_ThrowsUnavailableTimeout()
    {
        var handler = new FakeOllamaHandler().Hang();
        var provider = new OllamaProvider(new StubAiSettings(), new OllamaApiClient(handler, chatTimeout: TimeSpan.FromMilliseconds(50)));

        var act = () => provider.CompleteAsync(Request(), CancellationToken.None);

        (await act.Should().ThrowAsync<AiProviderUnavailableException>())
            .Which.Reason.Should().Be(AiProviderUnavailableReason.Timeout);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAsCancellationNotUnavailable()
    {
        var handler = new FakeOllamaHandler().Hang();
        var provider = Provider(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => provider.CompleteJsonAsync<int[]>(Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task IsConfigured_NeedsNoKey()
    {
        var provider = Provider(new FakeOllamaHandler());

        provider.ProviderName.Should().Be(AiDefaults.OllamaProvider);
        (await provider.IsConfiguredAsync(CancellationToken.None)).Should().BeTrue();
    }

    private static OllamaProvider Provider(FakeOllamaHandler handler) =>
        new(new StubAiSettings(), new OllamaApiClient(handler));

    private static AiRequest Request(int maxTokens = 256, double temperature = 0.3) =>
        new()
        {
            Provider = AiDefaults.OllamaProvider,
            Model = Model,
            Prompt = SecretPrompt,
            MaxTokens = maxTokens,
            Temperature = temperature,
        };

    private static AiToolRequest ToolRequest(params AiChatMessage[] messages) =>
        new()
        {
            Provider = AiDefaults.OllamaProvider,
            Model = Model,
            Messages = messages,
            Tools = [_spendingTool],
        };
}
