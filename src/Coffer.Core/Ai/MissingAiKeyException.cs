namespace Coffer.Core.Ai;

/// <summary>
/// Thrown by a provider when no API key is configured for the active vendor. Lets callers (the chat
/// service) distinguish a "configure your key in Settings" state from a genuine call failure without
/// matching on message text.
/// </summary>
public sealed class MissingAiKeyException : Exception
{
    public MissingAiKeyException(string message)
        : base(message)
    {
    }
}
