namespace Coffer.Core.Ai;

/// <summary>
/// Thrown by a provider whose service cannot serve the request right now — e.g. a local Ollama that is
/// not running, too slow, or does not have the chosen model. Consumers handle it like
/// <see cref="MissingAiKeyException"/>: nothing usable came back, the feature degrades (uncategorised,
/// engine-only / templated text, a chat reply that says AI is unavailable) and the call is not retried.
/// The message names the provider, endpoint and model only — never prompt text or anything the server
/// echoed back.
/// </summary>
public sealed class AiProviderUnavailableException : Exception
{
    public AiProviderUnavailableException(
        string provider,
        string endpoint,
        string? model,
        AiProviderUnavailableReason reason,
        Exception? innerException = null)
        : base(BuildMessage(provider, endpoint, model, reason), innerException)
    {
        Provider = provider;
        Endpoint = endpoint;
        Model = model;
        Reason = reason;
    }

    /// <summary>The provider that failed (e.g. <see cref="AiDefaults.OllamaProvider"/>).</summary>
    public string Provider { get; }

    /// <summary>The endpoint the request went to.</summary>
    public string Endpoint { get; }

    /// <summary>The model the request named, or <c>null</c> for a call that names none (a model listing).</summary>
    public string? Model { get; }

    public AiProviderUnavailableReason Reason { get; }

    private static string BuildMessage(string provider, string endpoint, string? model, AiProviderUnavailableReason reason)
    {
        var modelPart = string.IsNullOrEmpty(model) ? string.Empty : $" for model '{model}'";
        return reason switch
        {
            AiProviderUnavailableReason.Unreachable =>
                $"{provider} at {endpoint} could not be reached{modelPart}. Check that it is running.",
            AiProviderUnavailableReason.Timeout =>
                $"{provider} at {endpoint} did not answer in time{modelPart}.",
            AiProviderUnavailableReason.ModelNotFound =>
                $"{provider} at {endpoint} has no model '{model}'. Pull it or choose another model in Settings.",
            AiProviderUnavailableReason.ModelUnsupported =>
                $"{provider} at {endpoint}: model '{model}' does not support this request (e.g. tool calling).",
            _ => $"{provider} at {endpoint} rejected the request{modelPart}.",
        };
    }
}
