namespace Coffer.Core.Ai;

/// <summary>
/// A tool-calling completion request (Phase 7 chat). Unlike the single-prompt
/// <see cref="AiRequest"/> used by categorisation, chat is a multi-turn loop, so the running
/// conversation is carried as <see cref="Messages"/> and the available read-only tools as
/// <see cref="Tools"/>. The orchestrator re-sends the grown conversation each iteration until the
/// model returns a final text answer.
/// </summary>
public sealed record AiToolRequest
{
    public required string Model { get; init; }

    public required IReadOnlyList<AiChatMessage> Messages { get; init; }

    public string? SystemPrompt { get; init; }

    public IReadOnlyList<AiTool> Tools { get; init; } = [];

    public int MaxTokens { get; init; } = 1024;

    public double Temperature { get; init; } = 0.3;
}

/// <summary>
/// One model turn in the tool-call loop: a final text answer, a set of tool calls to execute, or
/// both. When <see cref="ToolCalls"/> is empty the turn is final and <see cref="Text"/> is the
/// answer.
/// </summary>
public sealed record AiToolTurn(string? Text, IReadOnlyList<AiToolCall> ToolCalls)
{
    public bool IsFinal => ToolCalls.Count == 0;
}
