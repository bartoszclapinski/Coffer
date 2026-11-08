namespace Coffer.Core.Ai;

/// <summary>
/// Thrown by the routing provider when a request names a provider that is not registered (e.g. a
/// stored route pointing at a provider this build does not have). Consumers treat it like
/// <see cref="MissingAiKeyException"/>: the AI feature is not configured, not a transient failure,
/// so it is never retried.
/// </summary>
public sealed class UnknownAiProviderException : Exception
{
    public UnknownAiProviderException(string provider)
        : base($"No AI provider named '{provider}' is available. Choose a provider in Settings.")
    {
        Provider = provider;
    }

    /// <summary>The provider name the request asked for.</summary>
    public string Provider { get; }
}
