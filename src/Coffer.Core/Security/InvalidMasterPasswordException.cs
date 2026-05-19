namespace Coffer.Core.Security;

/// <summary>
/// Thrown by <see cref="ILoginService.LoginWithPasswordAsync"/> when the AES-GCM
/// authentication tag on the DEK ciphertext does not match — overwhelmingly the
/// "wrong password" case. Carries no detail beyond the type so logs cannot leak
/// any part of the attempted password.
/// </summary>
public sealed class InvalidMasterPasswordException : Exception
{
    public InvalidMasterPasswordException()
        : base("The master password did not unlock the vault.")
    {
    }

    public InvalidMasterPasswordException(Exception inner)
        : base("The master password did not unlock the vault.", inner)
    {
    }
}
