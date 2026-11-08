namespace Coffer.Core.Ai;

/// <summary>
/// A single AI completion request. Tools and vision attachments (doc 04) arrive with
/// chat (Phase 7) and receipts (Phase 5); categorisation needs only a prompt, a model,
/// and an optional system prompt.
/// <para>
/// <see cref="Provider"/> is <c>required</c>, not defaulted: the routing provider dispatches on it,
/// and a forgotten provider defaulting to a remote vendor could send a prompt meant for a local model
/// off the machine. Consumers take it, with <see cref="Model"/>, from their resolved
/// <see cref="AiRoute"/>.
/// </para>
/// </summary>
public sealed record AiRequest
{
    public required string Prompt { get; init; }

    /// <summary>The registered provider that serves this request (e.g. <see cref="AiDefaults.ClaudeProvider"/>).</summary>
    public required string Provider { get; init; }

    public required string Model { get; init; }

    public string? SystemPrompt { get; init; }

    public int MaxTokens { get; init; } = 1000;

    public double Temperature { get; init; } = 0.3;
}
