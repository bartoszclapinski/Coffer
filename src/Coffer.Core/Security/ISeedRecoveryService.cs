namespace Coffer.Core.Security;

/// <summary>
/// Recovers or enables the BIP39-seed channel of the dual-wrapped DEK (doc 08 "Restore from seed", doc 09).
/// A seed-wrapped DEK lets the owner recover a vault after forgetting the master password, and lets an
/// existing password-only vault opt in. Never logs or returns any seed or key material (hard rule #6).
/// </summary>
public interface ISeedRecoveryService
{
    /// <summary>
    /// Whether the vault's DEK file carries a seed-wrapped copy (i.e. seed recovery is possible). Returns
    /// <c>false</c> when there is no vault or the DEK file is version 1 / unreadable.
    /// </summary>
    Task<bool> IsSeedRecoveryEnabledAsync(CancellationToken ct);

    /// <summary>
    /// Recovers the vault from a BIP39 seed and resets the master password: decrypts the DEK via the
    /// seed-wrapped blob, re-wraps it under <paramref name="newMasterPassword"/> (a fresh Argon2 salt),
    /// publishes the DEK, and refreshes the cache — effectively logging the owner in.
    /// </summary>
    /// <exception cref="SeedRecoveryUnavailableException">The vault has no seed-wrapped key (version 1).</exception>
    /// <exception cref="InvalidRecoverySeedException">The seed does not unlock the DEK.</exception>
    /// <exception cref="VaultMissingException">No DEK file exists.</exception>
    /// <exception cref="VaultCorruptedException">The DEK file is malformed or unreadable.</exception>
    Task RecoverWithSeedAsync(string mnemonic, string newMasterPassword, CancellationToken ct);

    /// <summary>
    /// Enables seed recovery on the current (logged-in) vault by wrapping the in-memory DEK with the recovery
    /// key derived from <paramref name="mnemonic"/> and rewriting the DEK file as dual-wrapped (version 2).
    /// Requires a published DEK (an unlocked session). The password-wrapped blob is left unchanged.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">No DEK is available (the session is locked).</exception>
    /// <exception cref="VaultMissingException">No DEK file exists.</exception>
    /// <exception cref="VaultCorruptedException">The DEK file is malformed or unreadable.</exception>
    Task EnableSeedRecoveryAsync(string mnemonic, CancellationToken ct);
}
