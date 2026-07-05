namespace Coffer.Core.Security;

/// <summary>
/// Thrown by <see cref="ISeedRecoveryService.RecoverWithSeedAsync"/> when the entered BIP39 seed does not
/// unlock the DEK — the AES-GCM tag on the seed-wrapped blob did not match. Overwhelmingly the "wrong words"
/// case. Carries no detail beyond the type so logs cannot leak any part of the attempted seed (hard rule #6).
/// </summary>
public sealed class InvalidRecoverySeedException : Exception
{
    public InvalidRecoverySeedException()
        : base("The recovery seed did not unlock the vault.")
    {
    }

    public InvalidRecoverySeedException(Exception inner)
        : base("The recovery seed did not unlock the vault.", inner)
    {
    }
}
