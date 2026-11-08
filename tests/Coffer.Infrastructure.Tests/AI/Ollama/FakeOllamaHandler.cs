using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI.Ollama;

/// <summary>
/// A scripted stand-in for an Ollama server: records every request and answers from a queue of
/// responses (status + body), an exception to throw, or a hang until the request is cancelled.
/// </summary>
internal sealed class FakeOllamaHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<(HttpMethod Method, Uri Uri, string Body)> Requests { get; } = [];

    public FakeOllamaHandler Respond(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));
        return this;
    }

    public FakeOllamaHandler Throw(Exception exception)
    {
        _responses.Enqueue((_, _) => throw exception);
        return this;
    }

    /// <summary>Never answers: the request ends only when its token (caller or timeout) is cancelled.</summary>
    public FakeOllamaHandler Hang()
    {
        _responses.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    public JsonObject SingleBody() =>
        JsonNode.Parse(Requests.Should().ContainSingle().Subject.Body)!.AsObject();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!, body));
        _responses.Should().NotBeEmpty("the test scripted a response for every request");
        return await _responses.Dequeue()(request, cancellationToken);
    }

    /// <summary>An <c>/api/chat</c> response with the given assistant message.</summary>
    public static string ChatResponse(JsonObject message, int promptTokens = 120, int evalTokens = 30) =>
        new JsonObject
        {
            ["model"] = "qwen2.5:3b",
            ["created_at"] = "2026-11-08T12:00:00Z",
            ["message"] = message,
            ["done"] = true,
            ["done_reason"] = "stop",
            ["prompt_eval_count"] = promptTokens,
            ["eval_count"] = evalTokens,
        }.ToJsonString();

    public static string TextResponse(string content, string? thinking = null)
    {
        var message = new JsonObject { ["role"] = "assistant", ["content"] = content };
        if (thinking is not null)
        {
            message["thinking"] = thinking;
        }

        return ChatResponse(message);
    }
}
