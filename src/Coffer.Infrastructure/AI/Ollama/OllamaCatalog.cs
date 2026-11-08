using System.Text.Json;
using System.Text.Json.Nodes;
using Coffer.Core.Ai;

namespace Coffer.Infrastructure.AI.Ollama;

/// <summary>
/// <see cref="IOllamaCatalog"/> over Ollama's <c>/api/version</c> and <c>/api/tags</c>, with the short
/// catalog timeout of <see cref="OllamaApiClient"/>. The model list carries each model's capabilities
/// when the server includes them in <c>/api/tags</c> (Ollama 0.40 does), so no per-model
/// <c>/api/show</c> call is made.
/// </summary>
public sealed class OllamaCatalog : IOllamaCatalog
{
    private readonly OllamaApiClient _client;

    public OllamaCatalog(OllamaApiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async Task<OllamaConnectionResult> TestConnectionAsync(Uri endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        try
        {
            var response = await _client.GetAsync(endpoint, "api/version", ct).ConfigureAwait(false);
            var version = response["version"]?.GetValueKind() == JsonValueKind.String
                ? response["version"]!.GetValue<string>()
                : null;
            // A JSON answer without a version is not an Ollama server.
            return string.IsNullOrWhiteSpace(version)
                ? OllamaConnectionResult.Failed(AiProviderUnavailableReason.Rejected)
                : OllamaConnectionResult.Connected(version);
        }
        catch (AiProviderUnavailableException ex)
        {
            return OllamaConnectionResult.Failed(ex.Reason);
        }
    }

    public async Task<IReadOnlyList<OllamaModelInfo>> ListModelsAsync(Uri endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var response = await _client.GetAsync(endpoint, "api/tags", ct).ConfigureAwait(false);
        if (response["models"] is not JsonArray models)
        {
            return [];
        }

        var result = new List<OllamaModelInfo>();
        foreach (var node in models.OfType<JsonObject>())
        {
            var name = StringOf(node["name"]) ?? StringOf(node["model"]);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var capabilities = node["capabilities"] is JsonArray array
                ? array.Select(StringOf).OfType<string>().ToList()
                : [];
            result.Add(new OllamaModelInfo(name, capabilities));
        }

        return result.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? StringOf(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;
}
