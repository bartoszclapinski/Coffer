namespace Coffer.Core.Security;

/// <summary>
/// Orchestrates the post-setup login flow. The cached-key path is the silent cold-start
/// path that satisfies the Phase-0 "restart within 7 days bypasses the password" goal;
/// the password path is the interactive fallback when no usable cache exists.
/// </summary>
public interface ILoginService
{
    /// <summary>
    /// Attempts to unlock the vault using only the DPAPI-cached master key. Returns
    /// <c>true</c> only when the cache hit, the DEK decrypted, and the DEK was published
    /// to <see cref="IDekHolder"/>. Any failure — cache miss, expired entry, decrypt
    /// error, missing files — returns <c>false</c> without throwing; the caller falls
    /// back to the interactive password path.
    /// </summary>
    Task<bool> TryLoginFromCachedKeyAsync(CancellationToken ct);

    /// <summary>
    /// Unlocks the vault using the user-supplied master password and refreshes the
    /// DPAPI cache with a full 7-day TTL on success.
    /// </summary>
    /// <exception cref="InvalidMasterPasswordException">The password did not unlock the vault.</exception>
    /// <exception cref="VaultCorruptedException">The DEK file could not be read.</exception>
    /// <exception cref="VaultMissingException">The DEK file is missing — App routing failed to catch this.</exception>
    Task LoginWithPasswordAsync(string masterPassword, CancellationToken ct);

    /// <summary>
    /// Clears the in-memory DEK and invalidates the DPAPI cache. Used by both manual
    /// logout (Wyloguj button) and the auto-lock timer.
    /// </summary>
    Task LogoutAsync(CancellationToken ct);
}
