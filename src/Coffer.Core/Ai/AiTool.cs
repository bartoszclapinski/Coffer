namespace Coffer.Core.Ai;

/// <summary>
/// A read-only tool the chat model may call to fetch real financial data (Phase 7, doc 04).
/// <see cref="ParametersJsonSchema"/> is a JSON Schema object describing the tool's arguments;
/// the provider maps this descriptor onto its vendor tool-calling mechanism. Tools never mutate
/// state — only the user can, through dedicated UI.
/// </summary>
public sealed record AiTool(string Name, string Description, string ParametersJsonSchema);

/// <summary>
/// One tool invocation the model requested in a turn: a vendor-assigned call id (used to pair the
/// result back), the tool name, and the raw JSON arguments object the model produced. The tool
/// layer parses and validates <see cref="ArgumentsJson"/>.
/// </summary>
public sealed record AiToolCall(string CallId, string ToolName, string ArgumentsJson);

/// <summary>
/// The serialized result of executing an <see cref="AiToolCall"/>, fed back to the model for the
/// next turn. <see cref="ResultJson"/> is already anonymised (hard rule #7) by the orchestrator
/// before it reaches the provider.
/// </summary>
public sealed record AiToolResult(string CallId, string ToolName, string ResultJson);
