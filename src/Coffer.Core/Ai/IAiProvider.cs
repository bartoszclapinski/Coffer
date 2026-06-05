namespace Coffer.Core.Ai;

/// <summary>
/// Provider-neutral AI completion surface (doc 04), implemented per vendor
/// (<c>ClaudeProvider</c> now, <c>OpenAiProvider</c> in 10-C) over
/// <c>Microsoft.Extensions.AI</c>. Categorisation uses only
/// <see cref="CompleteJsonAsync{TResult}"/>; <see cref="StreamAsync"/> is a stub until
/// chat lands in Phase 7. Every call returns token usage for the cost ledger.
/// </summary>
public interface IAiProvider
{
    /// <summary>Vendor label written to the ledger (e.g. <c>"Claude"</c>).</summary>
    string ProviderName { get; }

    Task<AiResult<string>> CompleteAsync(AiRequest request, CancellationToken ct);

    Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct);

    IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct);
}
