using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using Coffer.Core.Ai;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Applies a <see cref="IPromptRedactionSession"/> to the string values of a JSON document (chat
/// tool results and tool-call arguments). Redacting the raw JSON text instead would be unreliable:
/// the default serializer escapes non-ASCII and <c>+</c> (<c>Łukasz</c>, <c>+48…</c>), so a
/// name or phone would slip past the patterns, and a labelled value could run past its closing
/// quote. Property names are left untouched.
/// </summary>
public static class PromptJson
{
    // Keep Polish letters readable for the model (and fewer tokens); still escapes quotes, control
    // and HTML-sensitive characters.
    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>
    /// Redacts every string value. All strings are first passed through the session once so the
    /// counterparty names learnt from any labelled field (a transaction's description) are also
    /// caught where the same name appears unlabelled (the same row's <c>merchant</c>). Text that is
    /// not valid JSON is redacted as plain text.
    /// </summary>
    public static string Redact(string json, IPromptRedactionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrEmpty(json))
        {
            return json;
        }

        var root = TryParse(json);
        if (root is null)
        {
            return session.Redact(json);
        }

        if (IsString(root, out var single))
        {
            return JsonValue.Create(session.Redact(single)).ToJsonString(_writeOptions);
        }

        var strings = CollectStrings(root);
        foreach (var (value, _) in strings)
        {
            session.Redact(value);
        }

        foreach (var (value, replace) in strings)
        {
            replace(session.Redact(value));
        }

        return root.ToJsonString(_writeOptions);
    }

    /// <summary>
    /// Restores the session's pseudonyms in every string value; returns <paramref name="json"/>
    /// unchanged when nothing was restored, so untouched arguments keep their exact text.
    /// </summary>
    public static string Restore(string json, IPromptRedactionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrEmpty(json))
        {
            return json;
        }

        var root = TryParse(json);
        if (root is null)
        {
            return json;
        }

        var changed = false;
        foreach (var (value, replace) in CollectStrings(root))
        {
            var restored = session.Restore(value);
            if (!string.Equals(restored, value, StringComparison.Ordinal))
            {
                replace(restored);
                changed = true;
            }
        }

        return changed ? root.ToJsonString(_writeOptions) : json;
    }

    private static JsonNode? TryParse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<(string Value, Action<string> Replace)> CollectStrings(JsonNode root)
    {
        var result = new List<(string, Action<string>)>();
        Collect(root, result);
        return result;
    }

    private static void Collect(JsonNode node, List<(string, Action<string>)> result)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Select(p => p.Key).ToList())
                {
                    var child = obj[name];
                    if (IsString(child, out var value))
                    {
                        result.Add((value, replacement => obj[name] = replacement));
                    }
                    else if (child is not null)
                    {
                        Collect(child, result);
                    }
                }

                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var index = i;
                    var child = array[index];
                    if (IsString(child, out var value))
                    {
                        result.Add((value, replacement => array[index] = replacement));
                    }
                    else if (child is not null)
                    {
                        Collect(child, result);
                    }
                }

                break;
        }
    }

    private static bool IsString(JsonNode? node, out string value)
    {
        if (node is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.String)
        {
            value = jsonValue.GetValue<string>();
            return true;
        }

        value = string.Empty;
        return false;
    }
}
