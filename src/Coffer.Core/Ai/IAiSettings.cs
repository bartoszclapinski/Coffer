namespace Coffer.Core.Ai;

/// <summary>
/// User-configurable AI settings, persisted in the encrypted DB. The monthly cap feeds
/// the budget gate; each <see cref="AiRole"/> has its own provider + model (read through
/// <see cref="IAiRouteResolver"/> by the AI consumers). Sensible defaults apply until the
/// owner changes them: every role defaults to Claude with <see cref="AiDefaults.DefaultModel"/>.
/// </summary>
public interface IAiSettings
{
    Task<decimal> GetMonthlyCapPlnAsync(CancellationToken ct);

    Task SetMonthlyCapPlnAsync(decimal capPln, CancellationToken ct);

    /// <summary>
    /// The provider + model the role runs on, read together so the two always belong to the same
    /// saved route. Nothing stored (or the retired "OpenAI") reads as <see cref="AiDefaults.ClaudeProvider"/>;
    /// any other provider name is returned as stored, so a provider this build does not have fails
    /// closed at the router instead of silently moving to a remote vendor. For Claude a stored model
    /// that is no longer offered reads as the role's default; for another provider the stored model is
    /// returned as-is (empty when unset — the route is then not usable).
    /// </summary>
    Task<AiRoute> GetRouteAsync(AiRole role, CancellationToken ct);

    /// <summary>
    /// Saves the role's provider + model in one write. The provider must be one of
    /// <see cref="AiDefaults.Providers"/> and the model must not be empty.
    /// </summary>
    Task SetRouteAsync(AiRole role, AiRoute route, CancellationToken ct);

    /// <summary>
    /// The Ollama endpoint every Ollama call goes to. Nothing stored, or a stored value that is not a valid
    /// endpoint (<see cref="OllamaEndpoint.IsValid"/>), reads as <see cref="OllamaEndpoint.Default"/>.
    /// </summary>
    Task<Uri> GetOllamaEndpointAsync(CancellationToken ct);

    /// <summary>
    /// Saves the Ollama endpoint. Throws <see cref="ArgumentException"/> unless it is a valid endpoint
    /// (absolute <c>http</c> / <c>https</c>, no user info, query or fragment).
    /// </summary>
    Task SetOllamaEndpointAsync(Uri endpoint, CancellationToken ct);

    /// <summary>
    /// Whether the AI-assisted fallback parser may run for statements from banks with no
    /// deterministic parser. Off by default: it sends statement text to the AI provider, the
    /// most data-exposing AI feature in the app, so the owner must opt in explicitly.
    /// </summary>
    Task<bool> GetAiFallbackParsingEnabledAsync(CancellationToken ct);

    Task SetAiFallbackParsingEnabledAsync(bool enabled, CancellationToken ct);

    /// <summary>
    /// The account-holder name(s) to redact from a statement before it is sent to the AI
    /// fallback parser (the header carries the owner's name/address, which the account/IBAN/NIP
    /// rules do not cover). Stored as a single raw string (the owner may list several spellings or
    /// aliases separated by commas/newlines); <c>null</c> when the owner has not set it.
    /// </summary>
    Task<string?> GetOwnerIdentityNamesAsync(CancellationToken ct);

    Task SetOwnerIdentityNamesAsync(string? names, CancellationToken ct);
}
