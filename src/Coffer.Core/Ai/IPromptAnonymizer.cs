namespace Coffer.Core.Ai;

/// <summary>
/// The single choke point every AI-bound text passes through (hard rule #7). There is deliberately
/// no synchronous "anonymize this string" member: the only way to redact is to open a
/// <see cref="IPromptRedactionSession"/>, and opening one always loads the owner's identity names and
/// the known counterparty names. A caller therefore cannot forget to apply them.
/// </summary>
public interface IPromptAnonymizer
{
    /// <summary>
    /// Opens a redaction session for one AI request (or one chat question with its tool calls). Open
    /// it once per request and redact every piece of text that request sends through it, so the same
    /// counterparty maps to the same pseudonym throughout the request.
    /// </summary>
    Task<IPromptRedactionSession> BeginSessionAsync(CancellationToken ct);
}
