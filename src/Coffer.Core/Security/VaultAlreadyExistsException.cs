namespace Coffer.Core.Security;

/// <summary>
/// Thrown by <see cref="ISetupService"/> when one of the expected vault files
/// (<c>dek.encrypted</c> or <c>coffer.db</c>) already exists at the moment the
/// setup wizard tries to commit. The App routing should normally have detected
/// the state and shown a dedicated message window before the wizard runs;
/// receiving this exception from the wizard means the routing was bypassed or
/// the files appeared during the user's session.
/// </summary>
public sealed class VaultAlreadyExistsException : Exception
{
    public VaultAlreadyExistsException(string filePath)
        : base($"A vault file already exists at {filePath}.")
    {
        FilePath = filePath;
    }

    public VaultAlreadyExistsException(string filePath, Exception inner)
        : base($"A vault file already exists at {filePath}.", inner)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
}
