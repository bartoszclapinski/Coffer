namespace Coffer.Core.Ai;

/// <summary>
/// Provider-neutral AI completion surface (doc 04), implemented per vendor
/// (<c>ClaudeProvider</c> now, <c>OpenAiProvider</c> in 10-C) over
/// <c>Microsoft.Extensions.AI</c>. Categorisation uses only
/// <see cref="CompleteJsonAsync{TResult}"/>; chat (Phase 7) uses
/// <see cref="CompleteWithToolsAsync"/>; <see cref="StreamAsync"/> is a stub.
/// Every call returns token usage for the cost ledger.
/// </summary>
public interface IAiProvider
{
    /// <summary>Vendor label written to the ledger (e.g. <c>"Claude"</c>).</summary>
    string ProviderName { get; }

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
