namespace Coffer.Core.Ai;

/// <summary>
/// One model an Ollama endpoint serves. <see cref="Capabilities"/> holds the server's capability names
/// (e.g. <c>completion</c>, <c>tools</c>, <c>thinking</c>, <c>vision</c>); empty when the server version
/// does not report them.
/// </summary>
public sealed record OllamaModelInfo(string Name, IReadOnlyList<string> Capabilities)
{
    /// <summary>Whether the model can serve the chat tool loop; unknown (no capabilities reported) counts as yes.</summary>
    public bool SupportsTools => Capabilities.Count == 0 || Capabilities.Contains("tools", StringComparer.OrdinalIgnoreCase);
}
