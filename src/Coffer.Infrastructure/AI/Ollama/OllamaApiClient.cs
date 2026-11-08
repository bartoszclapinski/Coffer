using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coffer.Core.Ai;

namespace Coffer.Infrastructure.AI.Ollama;

/// <summary>
/// The one HTTP channel to Ollama's native API (<c>/api/chat</c>, <c>/api/tags</c>, <c>/api/version</c>),
/// shared by <see cref="OllamaProvider"/> and <see cref="OllamaCatalog"/>. Registered as a DI singleton, so
/// the process keeps one <see cref="HttpClient"/> (one connection pool) for its lifetime.
/// <para>
/// Every failure to get a usable answer becomes <see cref="AiProviderUnavailableException"/>: connection
/// refused / unknown host (<see cref="AiProviderUnavailableReason.Unreachable"/>), no answer within the
/// timeout (<see cref="AiProviderUnavailableReason.Timeout"/>), Ollama's own "model not found" 404 on a
/// model call (<see cref="AiProviderUnavailableReason.ModelNotFound"/>), a model that lacks a needed
/// capability (<see cref="AiProviderUnavailableReason.ModelUnsupported"/>), a prompt larger than the context
/// window (<see cref="AiProviderUnavailableReason.ContextTooLong"/>), any other non-success status — a bare
/// 404 from a wrong path prefix or another service included — or a body that is not a JSON object
/// (<see cref="AiProviderUnavailableReason.Rejected"/>). The exception names only the
/// endpoint and model; the server's error text is never copied into it, so nothing the server echoed from
/// a prompt can reach a log. Cancellation by the caller propagates as <see cref="OperationCanceledException"/>.
/// </para>
/// <para>
/// Redirects are not followed: a 3xx from the configured endpoint would otherwise re-send the prompt to
/// whatever host its <c>Location</c> names, past the loopback check on the configured endpoint. A redirect
/// surfaces as a non-success status (<see cref="AiProviderUnavailableReason.Rejected"/>).
/// </para>
/// </summary>
public sealed class OllamaApiClient : IDisposable
{
    /// <summary>Model calls: local models on a CPU can take minutes for a long analysis prompt.</summary>
    public static readonly TimeSpan DefaultChatTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Catalog calls (version, model list): the Settings screen must not hang on a dead endpoint.</summary>
    public static readonly TimeSpan DefaultCatalogTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly TimeSpan _chatTimeout;
    private readonly TimeSpan _catalogTimeout;

    /// <param name="httpHandler">Test seam: replaces the network handler. Production leaves it null.</param>
    /// <param name="chatTimeout">Test seam: overrides <see cref="DefaultChatTimeout"/>.</param>
    /// <param name="catalogTimeout">Test seam: overrides <see cref="DefaultCatalogTimeout"/>.</param>
    public OllamaApiClient(
        HttpMessageHandler? httpHandler = null,
        TimeSpan? chatTimeout = null,
        TimeSpan? catalogTimeout = null)
    {
        // The timeouts are enforced per call below (so a timeout is told apart from the caller's own
        // cancellation); the client-wide one is switched off. Redirects are not followed (see the remarks).
        _http = httpHandler is null
            ? new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                AllowAutoRedirect = false,
            })
            : new HttpClient(httpHandler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _chatTimeout = chatTimeout ?? DefaultChatTimeout;
        _catalogTimeout = catalogTimeout ?? DefaultCatalogTimeout;
    }

    /// <summary>Posts <paramref name="body"/> to <c>/api/chat</c> and returns the response object.</summary>
    public async Task<JsonObject> PostChatAsync(Uri endpoint, string model, JsonObject body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(body);

        using var request = new HttpRequestMessage(HttpMethod.Post, OllamaEndpoint.Combine(endpoint, "api/chat"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        return await SendAsync(request, endpoint, model, _chatTimeout, ct).ConfigureAwait(false);
    }

    /// <summary>GETs a catalog path (e.g. <c>api/tags</c>) with the short catalog timeout.</summary>
    public async Task<JsonObject> GetAsync(Uri endpoint, string apiPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        using var request = new HttpRequestMessage(HttpMethod.Get, OllamaEndpoint.Combine(endpoint, apiPath));
        return await SendAsync(request, endpoint, model: null, _catalogTimeout, ct).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();

    private async Task<JsonObject> SendAsync(
        HttpRequestMessage request, Uri endpoint, string? model, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        string body;
        HttpStatusCode status;
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token)
                .ConfigureAwait(false);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw Unavailable(endpoint, model, AiProviderUnavailableReason.Timeout, ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable(endpoint, model, AiProviderUnavailableReason.Unreachable, ex);
        }

        if ((int)status is < 200 or > 299)
        {
            throw Unavailable(endpoint, model, Classify(status, body, model), inner: null);
        }

        try
        {
            return JsonNode.Parse(body) as JsonObject
                ?? throw Unavailable(endpoint, model, AiProviderUnavailableReason.Rejected, inner: null);
        }
        catch (JsonException)
        {
            // Not Ollama (or a proxy error page). The parser's message is not kept: it may quote the body.
            throw Unavailable(endpoint, model, AiProviderUnavailableReason.Rejected, inner: null);
        }
    }

    // Ollama answers 404 {"error":"model 'x' not found"} for a model it does not have — a bare 404 (a wrong
    // reverse-proxy prefix, another service on the port: "404 page not found") is not that; 400 "... does
    // not support tools" / "... does not support thinking" when the model lacks a capability; and, because
    // every chat request sends truncate:false, 400 "request (N tokens) exceeds the available context size"
    // for a prompt that does not fit num_ctx. The body is only matched, never kept.
    private static AiProviderUnavailableReason Classify(HttpStatusCode status, string body, string? model)
    {
        if (status == HttpStatusCode.NotFound
            && model is not null
            && body.Contains("model", StringComparison.OrdinalIgnoreCase)
            && body.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return AiProviderUnavailableReason.ModelNotFound;
        }

        if (status == HttpStatusCode.BadRequest
            && body.Contains("does not support", StringComparison.OrdinalIgnoreCase))
        {
            return AiProviderUnavailableReason.ModelUnsupported;
        }

        if (status == HttpStatusCode.BadRequest
            && body.Contains("exceeds", StringComparison.OrdinalIgnoreCase)
            && body.Contains("context", StringComparison.OrdinalIgnoreCase))
        {
            return AiProviderUnavailableReason.ContextTooLong;
        }

        return AiProviderUnavailableReason.Rejected;
    }

    private static AiProviderUnavailableException Unavailable(
        Uri endpoint, string? model, AiProviderUnavailableReason reason, Exception? inner) =>
        new(AiDefaults.OllamaProvider, OllamaEndpoint.Format(endpoint), model, reason, inner);
}
