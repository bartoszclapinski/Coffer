namespace Coffer.Core.Ai;

/// <summary>
/// Provider-neutral AI completion surface (doc 04). Consumers depend on this interface and get the
/// routing provider, which dispatches each request to the <see cref="IAiProviderBackend"/> named by
/// the request's <c>Provider</c> (<c>ClaudeProvider</c> now; a local Ollama provider in Sprint 31).
/// Categorisation uses only <see cref="CompleteJsonAsync{TResult}"/>; chat (Phase 7) uses
/// <see cref="CompleteWithToolsAsync"/>; <see cref="StreamAsync"/> is a stub.
/// Every call returns token usage (with the provider that served it) for the cost ledger.
/// </summary>
public interface IAiProvider
{
    Task<AiResult<string>> CompleteAsync(AiRequest request, CancellationToken ct);

    Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct);

    /// <summary>
    /// Runs one tool-calling turn: sends the conversation plus the read-only tool menu and returns
    /// the model's next turn — either a final text answer or the tool calls it wants executed
    /// (<see cref="AiToolTurn"/>). The orchestrator (not the provider) executes the tools,
    /// anonymises and appends the results, and re-sends until the turn is final.
    /// </summary>
    Task<AiResult<AiToolTurn>> CompleteWithToolsAsync(AiToolRequest request, CancellationToken ct);

    IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct);
}
