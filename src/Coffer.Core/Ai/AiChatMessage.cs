namespace Coffer.Core.Ai;

/// <summary>Who authored a <see cref="AiChatMessage"/> in the tool-call conversation.</summary>
public enum AiChatRole
{
    User,
    Assistant,
    Tool,
}

/// <summary>
/// One message in the chat conversation sent to the provider. A <see cref="AiChatRole.User"/> or
/// <see cref="AiChatRole.Assistant"/> message carries <see cref="Text"/>; an assistant turn that
/// requested tools also carries <see cref="ToolCalls"/>; a <see cref="AiChatRole.Tool"/> message
/// carries the <see cref="ToolResults"/> fed back. The orchestrator grows this list each loop
/// iteration; the provider maps it onto its vendor message model.
/// </summary>
public sealed record AiChatMessage
{
    public required AiChatRole Role { get; init; }

    public string? Text { get; init; }

    public IReadOnlyList<AiToolCall> ToolCalls { get; init; } = [];

    public IReadOnlyList<AiToolResult> ToolResults { get; init; } = [];

    public static AiChatMessage User(string text) =>
        new() { Role = AiChatRole.User, Text = text };

    public static AiChatMessage Assistant(string? text, IReadOnlyList<AiToolCall> toolCalls) =>
        new() { Role = AiChatRole.Assistant, Text = text, ToolCalls = toolCalls };

    public static AiChatMessage ToolOutputs(IReadOnlyList<AiToolResult> results) =>
        new() { Role = AiChatRole.Tool, ToolResults = results };
}
