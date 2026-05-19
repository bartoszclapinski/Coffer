namespace Coffer.Core.Security;

/// <summary>
/// Thrown by <see cref="ILoginService.LoginWithPasswordAsync"/> when the vault files
/// exist but cannot be used because they are malformed, truncated, or otherwise
/// corrupted in a way unrelated to the master password being wrong. The
/// <see cref="Reason"/> distinguishes specific cases so the UI can show a more
/// useful Polish message.
/// </summary>
public sealed class VaultCorruptedException : Exception
{
    public VaultCorruptedException(VaultCorruptionReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public VaultCorruptedException(VaultCorruptionReason reason, string message, Exception inner)
        : base(message, inner)
    {
        Reason = reason;
    }

    public VaultCorruptionReason Reason { get; }
}
