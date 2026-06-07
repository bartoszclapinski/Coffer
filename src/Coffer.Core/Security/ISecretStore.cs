namespace Coffer.Core.Security;

/// <summary>
/// Encrypted storage for named string secrets — AI API keys today, OAuth refresh
/// tokens later. Distinct from <see cref="IKeyVault"/>, which holds the single TTL'd
/// master-key cache; secrets here are durable named values with no expiry. On Windows
/// the implementation is DPAPI-backed (<c>CurrentUser</c> scope), matching the
/// master-key cache. Values are never logged and never written in plaintext (hard
/// rules #6/#11).
/// </summary>
public interface ISecretStore
{
    /// <summary>Returns the secret for <paramref name="name"/>, or <c>null</c> if unset.</summary>
    Task<string?> GetSecretAsync(string name, CancellationToken ct);

    /// <summary>Stores (or overwrites) the secret for <paramref name="name"/>.</summary>
    Task SetSecretAsync(string name, string value, CancellationToken ct);

    /// <summary>Removes the secret for <paramref name="name"/>; a no-op if it is unset.</summary>
    Task DeleteSecretAsync(string name, CancellationToken ct);
}
