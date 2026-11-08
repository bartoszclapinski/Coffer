namespace Coffer.Core.Ai;

/// <summary>Why a provider could not serve a request (stable code; the display layer maps it to text).</summary>
public enum AiProviderUnavailableReason
{
    /// <summary>The endpoint could not be reached (connection refused, unknown host, network down).</summary>
    Unreachable,

    /// <summary>The endpoint did not answer within the request timeout.</summary>
    Timeout,

    /// <summary>The endpoint answered that the requested model does not exist (e.g. not pulled into Ollama).</summary>
    ModelNotFound,

    /// <summary>The model cannot do what the request needs (e.g. it does not support tool calling).</summary>
    ModelUnsupported,

    /// <summary>
    /// The request does not fit the model's context window (e.g. a long statement or chat history sent to a
    /// local model). Refused up front rather than silently cut, so nothing from the start of the prompt is lost.
    /// </summary>
    ContextTooLong,

    /// <summary>The endpoint answered with another non-success status.</summary>
    Rejected,
}
