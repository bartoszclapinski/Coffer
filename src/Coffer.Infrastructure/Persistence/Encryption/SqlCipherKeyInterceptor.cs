using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coffer.Infrastructure.Persistence.Encryption;

/// <summary>
/// Sets <c>PRAGMA key</c> on every opened connection so SQLCipher can decrypt the
/// database file.
/// </summary>
/// <remarks>
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
/// </remarks>
public sealed class SqlCipherKeyInterceptor : DbConnectionInterceptor, IDisposable
{
    private const string _commandPrefix = "PRAGMA key = \"x'";
    private const string _commandSuffix = "'\";";
    private const string _hexChars = "0123456789ABCDEF";

    private byte[]? _dek;

    public SqlCipherKeyInterceptor(byte[] dek)
    {
        ArgumentNullException.ThrowIfNull(dek);
        _dek = dek;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = BuildPragmaKeyCommand();
        cmd.ExecuteNonQuery();
        cmd.CommandText = string.Empty;

        base.ConnectionOpened(connection, eventData);
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

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_dek is not null)
        {
            Array.Clear(_dek, 0, _dek.Length);
            _dek = null;
        }
    }

    private string BuildPragmaKeyCommand()
    {
        var dek = _dek ?? throw new ObjectDisposedException(nameof(SqlCipherKeyInterceptor));

        Span<char> buffer = stackalloc char[_commandPrefix.Length + (dek.Length * 2) + _commandSuffix.Length];
        _commandPrefix.CopyTo(buffer);
        WriteHexUpper(dek, buffer.Slice(_commandPrefix.Length, dek.Length * 2));
        _commandSuffix.CopyTo(buffer[(_commandPrefix.Length + (dek.Length * 2))..]);

        var command = new string(buffer);
        buffer.Clear();
        return command;
    }

    private static void WriteHexUpper(ReadOnlySpan<byte> bytes, Span<char> destination)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            destination[i * 2] = _hexChars[bytes[i] >> 4];
            destination[(i * 2) + 1] = _hexChars[bytes[i] & 0xF];
        }
    }
}
