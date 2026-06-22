namespace Coffer.Core.Chat;

/// <summary>
/// Answers natural-language questions about the owner's finances (Phase 7, doc 04) by letting the
/// reasoning model call a fixed menu of <b>read-only</b> data tools — never by inventing numbers or
/// generating SQL. The conversation history is supplied by the caller (in-session memory for v1);
/// the service runs the tool-call loop, meters the cost ledger per turn, enforces the monthly
/// budget gate (chat is non-critical → blocked over cap), and anonymises tool output before it
/// returns to the model. Implemented in <c>Coffer.Infrastructure</c>; <c>Coffer.Core</c> stays free
/// of vendor SDK / EF (hard rule #3).
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Answers <paramref name="question"/> in the context of <paramref name="history"/> (prior
    /// turns this session, oldest first). Returns the assistant's reply plus the read-only tool
    /// calls that produced it, or a blocked turn when the budget cap is hit or no API key is set.
    /// </summary>
    Task<ChatTurn> AskAsync(
        string question,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct);
}
