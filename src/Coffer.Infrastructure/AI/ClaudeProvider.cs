using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic.SDK;
using Coffer.Core.Ai;
using Coffer.Core.Security;
using Microsoft.Extensions.AI;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// <see cref="IAiProvider"/> backed by Anthropic's Claude over the
/// <c>Microsoft.Extensions.AI</c> <see cref="IChatClient"/> surface (doc 04). The API key
/// is resolved per-call from <see cref="ISecretStore"/> so a key entered in Settings takes
/// effect without a restart, and so it never lives in a long-held field (hard rule #6).
/// Categorisation uses <see cref="CompleteJsonAsync{TResult}"/>; <see cref="StreamAsync"/>
/// is a stub until chat lands in Phase 7.
/// </summary>
public sealed class ClaudeProvider : IAiProvider
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISecretStore _secrets;

    public ClaudeProvider(ISecretStore secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        _secrets = secrets;
    }

    public string ProviderName => AiDefaults.ClaudeProvider;

    public async Task<AiResult<string>> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (text, usage) = await SendAsync(request, ct).ConfigureAwait(false);
        return new AiResult<string>(text, usage);
    }

    public async Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (text, usage) = await SendAsync(request, ct).ConfigureAwait(false);
        var json = ExtractJson(text);
        var value = JsonSerializer.Deserialize<TResult>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Claude returned a JSON null where a value was expected.");
        return new AiResult<TResult>(value, usage);
    }

    public async Task<AiResult<AiToolTurn>> CompleteWithToolsAsync(AiToolRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var apiKey = await _secrets.GetSecretAsync(AiDefaults.ClaudeApiKeySecret, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new MissingAiKeyException(
                "No Claude API key configured. Set it in Settings before using AI features.");
        }

        using var client = new AnthropicClient(new APIAuthentication(apiKey));
        IChatClient chat = client.Messages;

        var messages = BuildMessages(request);
        var options = new ChatOptions
        {
            ModelId = request.Model,
            MaxOutputTokens = request.MaxTokens,
            Temperature = (float)request.Temperature,
            Tools = request.Tools.Count == 0
                ? null
                : request.Tools.Select(t => (AITool)new SchemaToolFunction(t)).ToList(),
        };

        var response = await chat.GetResponseAsync(messages, options, ct).ConfigureAwait(false);

        var toolCalls = new List<AiToolCall>();
        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent call)
                {
                    toolCalls.Add(new AiToolCall(call.CallId, call.Name, SerializeArguments(call.Arguments)));
                }
            }
        }

        var inputTokens = (int)(response.Usage?.InputTokenCount ?? 0);
        var outputTokens = (int)(response.Usage?.OutputTokenCount ?? 0);
        var usage = new AiUsage(ProviderName, request.Model, inputTokens, outputTokens);
        var turn = new AiToolTurn(response.Text, toolCalls);
        return new AiResult<AiToolTurn>(turn, usage);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        AiRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Streaming chat arrives in Phase 7; categorisation never streams.
        ArgumentNullException.ThrowIfNull(request);
        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotSupportedException("ClaudeProvider streaming is not implemented until Phase 7 (chat).");
#pragma warning disable CS0162 // Unreachable — satisfies the iterator contract.
        yield break;
#pragma warning restore CS0162
    }

    private async Task<(string Text, AiUsage Usage)> SendAsync(AiRequest request, CancellationToken ct)
    {
        var apiKey = await _secrets.GetSecretAsync(AiDefaults.ClaudeApiKeySecret, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "No Claude API key configured. Set it in Settings before using AI features.");
        }

        using var client = new AnthropicClient(new APIAuthentication(apiKey));
        IChatClient chat = client.Messages;

        var messages = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, request.SystemPrompt));
        }

        messages.Add(new ChatMessage(ChatRole.User, request.Prompt));

        var options = new ChatOptions
        {
            ModelId = request.Model,
            MaxOutputTokens = request.MaxTokens,
            Temperature = (float)request.Temperature,
        };

        var response = await chat.GetResponseAsync(messages, options, ct).ConfigureAwait(false);

        var inputTokens = (int)(response.Usage?.InputTokenCount ?? 0);
        var outputTokens = (int)(response.Usage?.OutputTokenCount ?? 0);
        var usage = new AiUsage(ProviderName, request.Model, inputTokens, outputTokens);
        return (response.Text ?? string.Empty, usage);
    }

    private static List<ChatMessage> BuildMessages(AiToolRequest request)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, request.SystemPrompt));
        }

        foreach (var message in request.Messages)
        {
            switch (message.Role)
            {
                case AiChatRole.User:
                    messages.Add(new ChatMessage(ChatRole.User, message.Text ?? string.Empty));
                    break;

                case AiChatRole.Assistant:
                    var assistantContents = new List<AIContent>();
                    if (!string.IsNullOrEmpty(message.Text))
                    {
                        assistantContents.Add(new TextContent(message.Text));
                    }

                    foreach (var call in message.ToolCalls)
                    {
                        assistantContents.Add(
                            new FunctionCallContent(call.CallId, call.ToolName, DeserializeArguments(call.ArgumentsJson)));
                    }

                    messages.Add(new ChatMessage(ChatRole.Assistant, assistantContents));
                    break;

                case AiChatRole.Tool:
                    var toolContents = new List<AIContent>();
                    foreach (var result in message.ToolResults)
                    {
                        toolContents.Add(new FunctionResultContent(result.CallId, result.ResultJson));
                    }

                    messages.Add(new ChatMessage(ChatRole.Tool, toolContents));
                    break;
            }
        }

        return messages;
    }

    private static IDictionary<string, object?>? DeserializeArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson, _jsonOptions);
    }

    private static string SerializeArguments(IDictionary<string, object?>? arguments) =>
        arguments is null || arguments.Count == 0
            ? "{}"
            : JsonSerializer.Serialize(arguments, _jsonOptions);

    // Models sometimes wrap JSON in a ```json fence or add prose; take the outermost
    // brace/bracket span so deserialisation does not choke on the decoration.
    private static string ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var firstObj = text.IndexOf('{');
        var firstArr = text.IndexOf('[');
        var start = (firstObj, firstArr) switch
        {
            (-1, -1) => -1,
            (-1, var a) => a,
            (var o, -1) => o,
            var (o, a) => Math.Min(o, a),
        };

        if (start < 0)
        {
            return text.Trim();
        }

        var open = text[start];
        var close = open == '{' ? '}' : ']';
        var end = text.LastIndexOf(close);
        return end > start ? text[start..(end + 1)] : text.Trim();
    }

    /// <summary>
    /// Declares a read-only chat tool to the provider from a name + description + JSON-schema string.
    /// The orchestrator (<c>ChatService</c>) executes the tools itself — it never wraps the client in
    /// an auto-invoking decorator — so <see cref="InvokeCoreAsync"/> is never called.
    /// </summary>
    private sealed class SchemaToolFunction : AIFunction
    {
        private readonly string _name;
        private readonly string _description;
        private readonly JsonElement _schema;

        public SchemaToolFunction(AiTool tool)
        {
            _name = tool.Name;
            _description = tool.Description;
            using var doc = JsonDocument.Parse(tool.ParametersJsonSchema);
            _schema = doc.RootElement.Clone();
        }

        public override string Name => _name;

        public override string Description => _description;

        public override JsonElement JsonSchema => _schema;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "Chat tools are executed by ChatService, not auto-invoked by the provider.");
    }
}
