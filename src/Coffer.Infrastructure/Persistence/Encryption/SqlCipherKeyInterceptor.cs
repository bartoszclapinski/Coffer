using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coffer.Infrastructure.Persistence.Encryption;

/// <summary>
/// Sets <c>PRAGMA key</c> on every opened connection so SQLCipher can decrypt the
/// database file.
/// </summary>
/// <remarks>
/// Memory hygiene caveat: the DEK is held as a <see cref="byte"/>[] field for the
/// interceptor's lifetime, but <see cref="Convert.ToHexString(byte[])"/> produces an
/// immutable .NET string that lives in the managed heap until GC. The string is
/// rebuilt on every opened connection. This trades a known surface area for the
/// simplicity of using the standard ADO.NET command text path. See
/// <c>docs/architecture/09-security-key-management.md</c> §"Memory hygiene" and the
/// Sprint-4 code review for the rationale; a follow-up sprint may switch to a
/// <see cref="Span{T}"/>-based hex path that can be zeroed explicitly.
/// </remarks>
public sealed class SqlCipherKeyInterceptor : DbConnectionInterceptor
{
    private readonly byte[] _dek;

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

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private string BuildPragmaKeyCommand() =>
        $"PRAGMA key = \"x'{Convert.ToHexString(_dek)}'\";";
}
