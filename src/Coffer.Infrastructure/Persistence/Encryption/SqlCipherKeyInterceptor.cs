using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coffer.Infrastructure.Persistence.Encryption;

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
        SetPragmaKey(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        SetPragmaKey(connection);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private void SetPragmaKey(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA key = \"x'{Convert.ToHexString(_dek)}'\";";
        cmd.ExecuteNonQuery();
    }
}
