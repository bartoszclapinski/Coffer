namespace Coffer.Core.Ai;

/// <summary>
/// What the Settings screen needs from an Ollama endpoint before any model is chosen: whether it
/// answers, and which models it serves. Both calls take the endpoint explicitly (the owner may be
/// testing an address they have not saved yet) and use a short timeout, unlike chat calls.
/// </summary>
public interface IOllamaCatalog
{
    /// <summary>
    /// Asks the endpoint for its version (<c>GET /api/version</c>). Never throws for a failed connection
    /// — the result carries the reason; only cancellation propagates.
    /// </summary>
    Task<OllamaConnectionResult> TestConnectionAsync(Uri endpoint, CancellationToken ct);

    /// <summary>
    /// Lists the models the endpoint serves (<c>GET /api/tags</c>), sorted by name, with their capabilities
    /// when the server reports them. Throws <see cref="AiProviderUnavailableException"/> when the endpoint
    /// cannot be reached or answers with an error.
    /// </summary>
    Task<IReadOnlyList<OllamaModelInfo>> ListModelsAsync(Uri endpoint, CancellationToken ct);
}
