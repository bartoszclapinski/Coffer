namespace Coffer.Infrastructure.AI;

/// <summary>
/// Provider-neutral handling of a model's JSON answer, shared by every <c>IAiProviderBackend</c>.
/// </summary>
internal static class AiJson
{
    /// <summary>
    /// Models sometimes wrap JSON in a <c>```json</c> fence or add prose; take the outermost
    /// brace/bracket span so deserialisation does not choke on the decoration.
    /// </summary>
    public static string ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var firstObj = text.IndexOf('{');
        var firstArr = text.IndexOf('[');
        var start = (firstObj, firstArr) switch
        {
            (-1, -1) => -1,
            (-1, var a) => a,
            (var o, -1) => o,
            var (o, a) => Math.Min(o, a),
        };

        if (start < 0)
        {
            return text.Trim();
        }

        var open = text[start];
        var close = open == '{' ? '}' : ']';
        var end = text.LastIndexOf(close);
        return end > start ? text[start..(end + 1)] : text.Trim();
    }
}
