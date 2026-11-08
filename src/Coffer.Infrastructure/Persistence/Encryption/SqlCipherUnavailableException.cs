namespace Coffer.Infrastructure.Persistence.Encryption;

/// <summary>
/// Thrown by <see cref="SqlCipherKeyInterceptor"/> when a freshly opened database
/// connection is not served by the SQLCipher native library (<c>PRAGMA cipher_version</c>
/// returns nothing).
/// </summary>
/// <remarks>
/// This happens when a plain SQLite provider (e.g. <c>SQLitePCLRaw.bundle_e_sqlite3</c>)
/// wins the native-provider binding: plain SQLite silently ignores <c>PRAGMA key</c>, so
/// carrying on would create or read the vault in plaintext. The app fails closed instead.
/// The message never contains key material (hard rule #6).
/// </remarks>
public sealed class SqlCipherUnavailableException : InvalidOperationException
{
    public SqlCipherUnavailableException()
        : base(
            "SQLCipher native provider not loaded (PRAGMA cipher_version returned no value); "
            + "refusing to open or create the vault in plaintext. Ensure SQLitePCLRaw.bundle_e_sqlcipher "
            + "is the only SQLitePCLRaw bundle in the dependency graph.")
    {
    }
}
