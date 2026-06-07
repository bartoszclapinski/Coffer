namespace Coffer.Core.Ai;

/// <summary>
/// A single AI completion request. Tools and vision attachments (doc 04) arrive with
/// chat (Phase 7) and receipts (Phase 5); categorisation needs only a prompt, a model,
/// and an optional system prompt.
/// </summary>
public sealed record AiRequest
{
    public required string Prompt { get; init; }

    public required string Model { get; init; }

    public string? SystemPrompt { get; init; }

    public int MaxTokens { get; init; } = 1000;

    public double Temperature { get; init; } = 0.3;
}
