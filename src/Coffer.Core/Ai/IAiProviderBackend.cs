namespace Coffer.Core.Ai;

/// <summary>
/// A concrete vendor provider (Claude, Ollama) that the routing <see cref="IAiProvider"/> dispatches
/// to by <see cref="AiRequest.Provider"/> / <see cref="AiToolRequest.Provider"/>. Backends are
/// registered under this interface, never as <see cref="IAiProvider"/>, so consumers always get the
/// router and the router never resolves itself.
/// </summary>
public interface IAiProviderBackend : IAiProvider
{
    /// <summary>The provider name routes refer to and the ledger records (e.g. <c>"Claude"</c>).</summary>
    string ProviderName { get; }

    /// <summary>
    /// Whether the provider's own prerequisites are met without making a call (e.g. an API key is
    /// stored). A local provider with no prerequisites returns <c>true</c>.
    /// </summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct);
}
