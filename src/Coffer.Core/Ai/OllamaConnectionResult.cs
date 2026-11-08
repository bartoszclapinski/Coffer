namespace Coffer.Core.Ai;

/// <summary>
/// The outcome of <see cref="IOllamaCatalog.TestConnectionAsync"/>: the server version on success, the
/// failure reason otherwise.
/// </summary>
public sealed record OllamaConnectionResult(string? Version, AiProviderUnavailableReason? Failure)
{
    public bool IsSuccess => Failure is null;

    public static OllamaConnectionResult Connected(string version) => new(version, null);

    public static OllamaConnectionResult Failed(AiProviderUnavailableReason reason) => new(null, reason);
}
