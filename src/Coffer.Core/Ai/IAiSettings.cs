namespace Coffer.Core.Ai;

/// <summary>
/// User-configurable AI settings, persisted in the encrypted DB. The monthly cap feeds
/// the budget gate; the active provider and categorisation model drive which vendor and
/// model categorisation uses. Sensible defaults apply until the owner changes them.
/// </summary>
public interface IAiSettings
{
    Task<decimal> GetMonthlyCapPlnAsync(CancellationToken ct);

    Task SetMonthlyCapPlnAsync(decimal capPln, CancellationToken ct);

    Task<string> GetActiveProviderAsync(CancellationToken ct);

    Task SetActiveProviderAsync(string provider, CancellationToken ct);

    Task<string> GetCategorizationModelAsync(CancellationToken ct);

    Task SetCategorizationModelAsync(string model, CancellationToken ct);

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
