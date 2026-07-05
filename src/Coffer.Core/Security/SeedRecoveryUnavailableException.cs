namespace Coffer.Core.Security;

/// <summary>
/// Thrown by <see cref="ISeedRecoveryService.RecoverWithSeedAsync"/> when the vault's DEK file predates
/// dual-wrap (version 1) and therefore carries no seed-wrapped copy of the DEK. Such a vault cannot be
/// recovered from a seed — the owner must use their master password and can then enable seed recovery.
/// Carries no key material.
/// </summary>
public sealed class SeedRecoveryUnavailableException : Exception
{
    public SeedRecoveryUnavailableException()
        : base("This vault has no seed-wrapped key, so it cannot be recovered from a seed.")
    {
    }
}
