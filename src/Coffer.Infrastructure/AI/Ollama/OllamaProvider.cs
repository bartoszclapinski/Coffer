using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Coffer.Core.Ai;

namespace Coffer.Infrastructure.AI.Ollama;

/// <summary>
/// <see cref="IAiProviderBackend"/> over a local (or self-hosted) Ollama's native <c>/api/chat</c>
/// (Sprint 31-C). The endpoint is read from <see cref="IAiSettings"/> on every call, so a change in Settings
/// applies to the next request; no API key is needed. Every request is non-streaming and sets the context
/// window explicitly (<see cref="ContextWindowTokens"/>) — Ollama's default context is small and silently
/// truncates long prompts — together with <c>num_predict</c> (the request's max tokens) and
/// <c>temperature</c>.
/// <para>
/// JSON completions send a JSON schema generated from the result type as <c>format</c> and
/// <c>think: false</c>. A schema, not <c>format: "json"</c>: verified against Ollama 0.40 with
/// <c>qwen2.5:3b</c> and <c>qwen3.5</c>, <c>"json"</c> forces a JSON <em>object</em>, so a prompt that asks
/// for an array (categorisation's index array) came back as <c>{"0":0,"1":1}</c>; the schema keeps the
/// answer in the shape the caller deserialises. <c>think: false</c> keeps a thinking model fast and its
/// answer in <c>content</c>; a model without thinking accepts it unchanged (only <c>think: true</c> is
/// rejected there). Text and tool turns leave thinking at the model's default. The answer is always read
/// from <c>message.content</c>, never from <c>message.thinking</c>.
/// </para>
/// <para>
/// Prompts reach this provider already redacted by the caller's session (hard rule #7) — the same code
/// path as Claude, because the endpoint may point at another machine. Nothing here logs prompts or answers.
/// </para>
/// </summary>
public sealed class OllamaProvider : IAiProviderBackend
{
    /// <summary>The context window every request asks for (<c>options.num_ctx</c>).</summary>
    public const int ContextWindowTokens = 16384;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    // Schema generation only: strict numbers, so an int is "integer" rather than "integer or numeric
    // string" (the Web defaults read numbers from strings, which would loosen the grammar).
    private static readonly JsonSerializerOptions _schemaOptions = CreateSchemaOptions();

    private static readonly ConcurrentDictionary<Type, JsonNode> _schemaCache = new();

    private readonly IAiSettings _settings;
    private readonly OllamaApiClient _client;

    public OllamaProvider(IAiSettings settings, OllamaApiClient client)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(client);
        _settings = settings;
        _client = client;
    }

    public string ProviderName => AiDefaults.OllamaProvider;

    /// <summary>Ollama needs no key; whether it is running is only known by calling it.</summary>
    public Task<bool> IsConfiguredAsync(CancellationToken ct) => Task.FromResult(true);

    public async Task<AiResult<string>> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = BuildBody(request.Model, BuildMessages(request), request.MaxTokens, request.Temperature);
        var (message, usage) = await SendAsync(request.Model, body, ct).ConfigureAwait(false);
        return new AiResult<string>(ContentOf(message) ?? string.Empty, usage);
    }

    public async Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = BuildBody(request.Model, BuildMessages(request), request.MaxTokens, request.Temperature);
        body["format"] = FormatFor<TResult>();
        body["think"] = false;

        var (message, usage) = await SendAsync(request.Model, body, ct).ConfigureAwait(false);
        var content = ContentOf(message);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Ollama returned an empty answer where JSON was expected.");
        }

        var value = JsonSerializer.Deserialize<TResult>(AiJson.ExtractJson(content), _jsonOptions)
            ?? throw new InvalidOperationException("Ollama returned a JSON null where a value was expected.");
        return new AiResult<TResult>(value, usage);
    }

    public async Task<AiResult<AiToolTurn>> CompleteWithToolsAsync(AiToolRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var messages = new JsonArray();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(Message("system", request.SystemPrompt));
        }

        foreach (var message in request.Messages)
        {
            AppendConversationMessage(messages, message);
        }

        var body = BuildBody(request.Model, messages, request.MaxTokens, request.Temperature);
        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(request.Tools.Select(ToolDefinition).ToArray<JsonNode?>());
        }

        var (reply, usage) = await SendAsync(request.Model, body, ct).ConfigureAwait(false);

        var toolCalls = ReadToolCalls(reply);
        var text = ContentOf(reply);
        if (toolCalls.Count == 0 && string.IsNullOrWhiteSpace(text))
        {
            // E.g. a thinking model that spent num_predict on reasoning: no answer and nothing to run.
            throw new InvalidOperationException("Ollama returned neither an answer nor a tool call.");
        }

        var turn = new AiToolTurn(string.IsNullOrEmpty(text) ? null : text, toolCalls);
        return new AiResult<AiToolTurn>(turn, usage);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        AiRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Streaming chat is out of scope for Sprint 31, as for Claude.
        ArgumentNullException.ThrowIfNull(request);
        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotSupportedException("OllamaProvider streaming is not implemented.");
#pragma warning disable CS0162 // Unreachable — satisfies the iterator contract.
        yield break;
#pragma warning restore CS0162
    }

    private async Task<(JsonObject Message, AiUsage Usage)> SendAsync(string model, JsonObject body, CancellationToken ct)
    {
        var endpoint = await _settings.GetOllamaEndpointAsync(ct).ConfigureAwait(false);
        var response = await _client.PostChatAsync(endpoint, model, body, ct).ConfigureAwait(false);

        var message = response["message"] as JsonObject
            ?? throw new InvalidOperationException("Ollama returned a response without a message.");
        var usage = new AiUsage(
            ProviderName,
            model,
            ReadCount(response, "prompt_eval_count"),
            ReadCount(response, "eval_count"));
        return (message, usage);
    }

    private static JsonObject BuildBody(string model, JsonArray messages, int maxTokens, double temperature) =>
        new()
        {
            ["model"] = model,
            ["stream"] = false,
            ["messages"] = messages,
            ["options"] = new JsonObject
            {
                ["num_ctx"] = ContextWindowTokens,
                ["num_predict"] = maxTokens,
                ["temperature"] = temperature,
            },
        };

    private static JsonArray BuildMessages(AiRequest request)
    {
        var messages = new JsonArray();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(Message("system", request.SystemPrompt));
        }

        messages.Add(Message("user", request.Prompt));
        return messages;
    }

    private static void AppendConversationMessage(JsonArray messages, AiChatMessage message)
    {
        switch (message.Role)
        {
            case AiChatRole.User:
                messages.Add(Message("user", message.Text ?? string.Empty));
                break;

            case AiChatRole.Assistant:
                var assistant = Message("assistant", message.Text ?? string.Empty);
                if (message.ToolCalls.Count > 0)
                {
                    assistant["tool_calls"] = new JsonArray(message.ToolCalls
                        .Select(call => (JsonNode?)new JsonObject
                        {
                            ["id"] = call.CallId,
                            ["function"] = new JsonObject
                            {
                                ["name"] = call.ToolName,
                                ["arguments"] = ArgumentsObject(call.ArgumentsJson),
                            },
                        })
                        .ToArray());
                }

                messages.Add(assistant);
                break;

            case AiChatRole.Tool:
                // One "tool" message per result; tool_name is what Ollama pairs on, tool_call_id is kept for
                // servers that also match by id.
                foreach (var result in message.ToolResults)
                {
                    var tool = Message("tool", result.ResultJson);
                    tool["tool_name"] = result.ToolName;
                    tool["tool_call_id"] = result.CallId;
                    messages.Add(tool);
                }

                break;
        }
    }

    private static JsonObject Message(string role, string content) =>
        new() { ["role"] = role, ["content"] = content };

    private static JsonNode ToolDefinition(AiTool tool) =>
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = JsonNode.Parse(tool.ParametersJsonSchema),
            },
        };

    // Ollama takes tool-call arguments as an object, not as JSON text.
    private static JsonObject ArgumentsObject(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(argumentsJson) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<AiToolCall> ReadToolCalls(JsonObject message)
    {
        var calls = new List<AiToolCall>();
        if (message["tool_calls"] is not JsonArray toolCalls)
        {
            return calls;
        }

        foreach (var node in toolCalls)
        {
            if (node?["function"] is not JsonObject function
                || function["name"]?.GetValueKind() != JsonValueKind.String)
            {
                continue;
            }

            var name = function["name"]!.GetValue<string>();
            // Older Ollama versions send no id; the orchestrator pairs results by id, so one is made up.
            var id = node["id"]?.GetValueKind() == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(node["id"]!.GetValue<string>())
                    ? node["id"]!.GetValue<string>()
                    : $"call_{Guid.NewGuid():N}";
            calls.Add(new AiToolCall(id, name, ArgumentsText(function["arguments"])));
        }

        return calls;
    }

    // Arguments arrive as an object; tolerate a JSON string holding the object.
    private static string ArgumentsText(JsonNode? arguments) =>
        arguments?.GetValueKind() switch
        {
            JsonValueKind.Object => arguments.ToJsonString(),
            JsonValueKind.String when !string.IsNullOrWhiteSpace(arguments.GetValue<string>()) => arguments.GetValue<string>(),
            _ => "{}",
        };

    private static string? ContentOf(JsonObject message) =>
        message["content"]?.GetValueKind() == JsonValueKind.String ? message["content"]!.GetValue<string>() : null;

    private static int ReadCount(JsonObject response, string name) =>
        response[name]?.GetValueKind() == JsonValueKind.Number && response[name]!.AsValue().TryGetValue<int>(out var count)
            ? count
            : 0;

    // A JSON schema of the result type as the "format" constraint; "json" only if the type cannot be
    // described (the answer is still parsed leniently either way).
    private static JsonNode FormatFor<TResult>()
    {
        try
        {
            return _schemaCache.GetOrAdd(typeof(TResult), CreateSchema).DeepClone();
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return JsonValue.Create("json")!; // Non-null for a non-null string.
        }
    }

    private static JsonNode CreateSchema(Type type) =>
        _schemaOptions.GetJsonSchemaAsNode(type, new JsonSchemaExporterOptions
        {
            TransformSchemaNode = (context, node) =>
            {
                if (node is not JsonObject schema)
                {
                    return node;
                }

                // Every property is required (nullable ones may still be null), so the constrained answer
                // always has the full shape the prompt describes instead of stopping at "{}".
                if (schema["properties"] is JsonObject properties)
                {
                    schema["required"] = new JsonArray(properties.Select(p => (JsonNode?)p.Key).ToArray());
                }

                // The root answer itself must not be null.
                if (context.Path.IsEmpty && schema["type"] is JsonArray types)
                {
                    var nonNull = types.Where(t => t?.GetValue<string>() != "null").Select(t => t!.GetValue<string>()).ToList();
                    schema["type"] = nonNull.Count == 1
                        ? JsonValue.Create(nonNull[0])
                        : new JsonArray(nonNull.Select(t => (JsonNode?)t).ToArray());
                }

                return schema;
            },
        });

    private static JsonSerializerOptions CreateSchemaOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.MakeReadOnly();
        return options;
    }
}
