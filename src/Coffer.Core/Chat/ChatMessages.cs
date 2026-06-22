namespace Coffer.Core.Chat;

/// <summary>Author of a chat message shown in the UI.</summary>
public enum ChatAuthor
{
    User,
    Assistant,
}

/// <summary>
/// One executed tool call surfaced to the UI as a transparency trace (doc 04 "Tool transparency"):
/// the tool's name and the concrete arguments the model passed. Lets the owner see that answers
/// come from real read-only queries, not invented numbers.
/// </summary>
public sealed record ChatToolTrace(string ToolName, string ArgumentsJson);

/// <summary>
/// One turn in the conversation as the UI sees it: who spoke, the text, and (for assistant turns)
/// the tools that ran to produce it.
/// </summary>
public sealed record ChatMessage(
    ChatAuthor Author,
    string Text,
    IReadOnlyList<ChatToolTrace> ToolTraces);

/// <summary>
/// Outcome of a single user question. <see cref="Answer"/> is the assistant's reply text;
/// <see cref="ToolTraces"/> are the read-only tools that ran. <see cref="BudgetExceeded"/> is set
/// when the monthly cap blocked the call (no API request was made); <see cref="MissingApiKey"/>
/// when no chat API key is configured. In both blocked cases <see cref="Answer"/> carries a
/// friendly Polish explanation and no tools ran.
/// </summary>
public sealed record ChatTurn(
    string Answer,
    IReadOnlyList<ChatToolTrace> ToolTraces,
    bool BudgetExceeded = false,
    bool MissingApiKey = false);
