using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coffer.Infrastructure.Persistence.Encryption;

/// <summary>
/// Sets <c>PRAGMA key</c> on every opened connection so SQLCipher can decrypt the
/// database file, then verifies that SQLCipher is actually the engine serving the
/// connection (fail closed — see <see cref="EnsureSqlCipherActive"/>).
/// </summary>
/// <remarks>
/// <para>
/// Fail-closed guard: plain SQLite silently ignores <c>PRAGMA key</c>, so if a
/// non-SQLCipher native provider ever wins the SQLitePCLRaw binding (e.g. a transitive
/// <c>bundle_e_sqlite3</c> with a higher version than <c>bundle_e_sqlcipher</c>) the vault
/// would be written in plaintext with no error. After setting the key the interceptor runs
/// <c>PRAGMA cipher_version</c> — answered by SQLCipher without touching the database file,
/// and returning no row on plain SQLite — and throws <see cref="SqlCipherUnavailableException"/>
/// before EF can create or read a single table. It runs on every open (not once per process)
/// because it costs one in-memory pragma and cannot be fooled by a provider swap mid-process.
/// </para>
/// <para>
/// Memory hygiene: the DEK is held as a <see cref="byte"/>[] field for the
/// interceptor's lifetime and zeroed on <see cref="Dispose"/>. The PRAGMA command
/// is built into a stack-allocated <see cref="Span{T}"/> that is cleared right
/// after the command string is materialised, so no intermediate hex
/// <see cref="string"/> (as <see cref="Convert.ToHexString(byte[])"/> would
/// produce) ever lands on the managed heap. One residual leak remains and cannot
/// be avoided with the current Microsoft.Data.Sqlite surface: the
/// <see cref="DbCommand.CommandText"/> setter takes an immutable <see cref="string"/>,
/// so the final command lives on the heap until GC. We null it out after execution
/// to drop the reference as early as possible. SqlCipher does not accept the key as
/// a bound parameter (PRAGMA statements cannot be parameterised in SQLite), so a
/// parameterised path is not an option. See
/// <c>docs/architecture/09-security-key-management.md</c> §"Memory hygiene".
/// </para>
/// </remarks>
public sealed class SqlCipherKeyInterceptor : DbConnectionInterceptor, IDisposable
{
    private const string CommandPrefix = "PRAGMA key = \"x'";
    private const string CommandSuffix = "'\";";
    private const string HexChars = "0123456789ABCDEF";
    private const string CipherVersionCommand = "PRAGMA cipher_version;";

    private byte[]? _dek;

    public SqlCipherKeyInterceptor(byte[] dek)
    {
        ArgumentNullException.ThrowIfNull(dek);
        _dek = dek;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);

        KeyConnection(connection, _dek ?? throw new ObjectDisposedException(nameof(SqlCipherKeyInterceptor)));

        base.ConnectionOpened(connection, eventData);
    }

    /// <summary>
    /// Keys an already-open connection with <paramref name="dek"/> and fails closed exactly like the
    /// interceptor does. Used by code paths that open their own raw connections outside EF — the backup
    /// snapshot writer (online backup API) — so they can never read or write the vault in plaintext.
    /// </summary>
    internal static void KeyConnection(DbConnection connection, byte[] dek)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(dek);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = BuildPragmaKeyCommand(dek);
        cmd.ExecuteNonQuery();
        cmd.CommandText = string.Empty;

        cmd.CommandText = CipherVersionCommand;
        try
        {
            EnsureSqlCipherActive(cmd.ExecuteScalar());
        }
        catch (SqlCipherUnavailableException)
        {
            // Close before propagating so nobody who catches the exception can keep using a
            // connection that would read or write plaintext.
            connection.Close();
            throw;
        }
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = BuildPragmaKeyCommand();
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        cmd.CommandText = string.Empty;

        cmd.CommandText = CipherVersionCommand;
        try
        {
            EnsureSqlCipherActive(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (SqlCipherUnavailableException)
        {
            // Close before propagating so nobody who catches the exception can keep using a
            // connection that would read or write plaintext.
            await connection.CloseAsync().ConfigureAwait(false);
            throw;
        }

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Throws <see cref="SqlCipherUnavailableException"/> unless <paramref name="cipherVersion"/>
    /// — the scalar result of <c>PRAGMA cipher_version</c> on an open connection — is a
    /// non-empty version string. Plain SQLite answers the pragma with no row, which ADO.NET
    /// surfaces as <see langword="null"/> (or <see cref="DBNull"/>).
    /// </summary>
    /// <param name="cipherVersion">The raw <see cref="DbCommand.ExecuteScalar"/> result.</param>
    public static void EnsureSqlCipherActive(object? cipherVersion)
    {
        if (cipherVersion is not string version || string.IsNullOrWhiteSpace(version))
        {
            throw new SqlCipherUnavailableException();
        }
    }

    public void Dispose()
    {
        if (_dek is not null)
        {
            Array.Clear(_dek, 0, _dek.Length);
            _dek = null;
        }
    }

    private string BuildPragmaKeyCommand() =>
        BuildPragmaKeyCommand(_dek ?? throw new ObjectDisposedException(nameof(SqlCipherKeyInterceptor)));

    private static string BuildPragmaKeyCommand(byte[] dek)
    {
        Span<char> buffer = stackalloc char[CommandPrefix.Length + (dek.Length * 2) + CommandSuffix.Length];
        CommandPrefix.CopyTo(buffer);
        WriteHexUpper(dek, buffer.Slice(CommandPrefix.Length, dek.Length * 2));
        CommandSuffix.CopyTo(buffer[(CommandPrefix.Length + (dek.Length * 2))..]);

        var command = new string(buffer);
        buffer.Clear();
        return command;
    }

    private static void WriteHexUpper(ReadOnlySpan<byte> bytes, Span<char> destination)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            destination[i * 2] = HexChars[bytes[i] >> 4];
            destination[(i * 2) + 1] = HexChars[bytes[i] & 0xF];
        }
    }
}
