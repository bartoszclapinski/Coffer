namespace Coffer.Infrastructure.Security;

/// <summary>
/// Shared constants for deriving the BIP39 recovery key that wraps the DEK (doc 09). The passphrase is a
/// fixed domain-separation value — not a secret — used identically wherever the seed wraps or unwraps the
/// DEK (setup, enable-seed-recovery, restore-from-seed) so all three agree. It is frozen once any v2 vault
/// exists: changing it would make existing seed-wrapped DEKs unrecoverable.
/// </summary>
internal static class VaultSeedDerivation
{
    public const string Passphrase = "Coffer";
}
