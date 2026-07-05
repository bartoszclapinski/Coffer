namespace Coffer.Core.Security;

/// <summary>
/// Rotates the master password (doc 09: "change master password → re-encrypt DEK only, not the entire DB").
/// Re-wraps only the password-wrapped copy of the DEK under a new key; the database, the DEK, and the
/// seed-wrapped copy are untouched. Never logs or returns any password or key (hard rule #6).
/// </summary>
public interface IMasterPasswordService
{
    /// <summary>
    /// Verifies <paramref name="currentPassword"/> (by decrypting the password blob), then re-wraps the DEK
    /// under <paramref name="newPassword"/> (a fresh Argon2 salt), rewrites the DEK file preserving its
    /// version and seed blob, and refreshes the cached master key. The in-memory DEK is unchanged.
    /// </summary>
    /// <exception cref="InvalidMasterPasswordException">The current password is wrong.</exception>
    /// <exception cref="VaultMissingException">No DEK file exists.</exception>
    /// <exception cref="VaultCorruptedException">The DEK file is malformed or unreadable.</exception>
    Task ChangeMasterPasswordAsync(string currentPassword, string newPassword, CancellationToken ct);
}
