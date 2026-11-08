using System.Diagnostics.CodeAnalysis;

namespace Coffer.Core.Ai;

/// <summary>
/// Validation and inspection of an Ollama endpoint address (Sprint 31). An endpoint is an absolute
/// <c>http</c> / <c>https</c> URI with a host and no user info, query or fragment — user info would put a
/// credential in the settings table and in logs. A path is allowed (an Ollama behind a reverse-proxy
/// prefix); the API paths are appended to it.
/// </summary>
public static class OllamaEndpoint
{
    /// <summary>The endpoint used until the owner saves another: a local Ollama on its default port.</summary>
    public static Uri Default { get; } = new(AiDefaults.OllamaEndpoint);

    /// <summary>
    /// Parses <paramref name="text"/> as an endpoint. Returns <c>false</c> (and a <c>null</c>
    /// <paramref name="endpoint"/>) for anything that is not a valid endpoint per the rules above.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out Uri? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(text)
            || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!IsValid(uri))
        {
            return false;
        }

        endpoint = uri;
        return true;
    }

    /// <summary>Whether <paramref name="endpoint"/> satisfies the endpoint rules.</summary>
    public static bool IsValid(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.IsAbsoluteUri
            && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(endpoint.Host)
            && string.IsNullOrEmpty(endpoint.UserInfo)
            && string.IsNullOrEmpty(endpoint.Query)
            && string.IsNullOrEmpty(endpoint.Fragment);
    }

    /// <summary>
    /// Whether the endpoint's host is this machine (<c>localhost</c>, <c>127.0.0.0/8</c>, <c>::1</c>).
    /// Anything else means prompts leave the machine (still redacted), which Settings warns about.
    /// </summary>
    public static bool IsLoopback(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.IsAbsoluteUri && endpoint.IsLoopback;
    }

    /// <summary>The canonical text form stored in settings: the absolute URI without a trailing slash.</summary>
    public static string Format(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.AbsoluteUri.TrimEnd('/');
    }

    /// <summary>The absolute URI of an API path (e.g. <c>api/chat</c>) under the endpoint, keeping any path prefix.</summary>
    public static Uri Combine(Uri endpoint, string apiPath)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiPath);
        return new Uri(Format(endpoint) + "/" + apiPath.TrimStart('/'));
    }
}
