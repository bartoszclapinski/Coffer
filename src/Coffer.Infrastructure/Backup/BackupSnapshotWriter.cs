using Coffer.Core.Security;
using Coffer.Infrastructure.Persistence.Encryption;
using Microsoft.Data.Sqlite;

namespace Coffer.Infrastructure.Backup;

/// <summary>
/// Shared snapshot writer for every backup path (daily/manual snapshots, the archive export, the pre-migration
/// copy, the restore swap). Two strategies:
/// <list type="bullet">
/// <item><b>Online backup</b> (preferred, whenever the DEK is held): the source is opened on its own keyed,
/// non-pooled connection and copied with SQLite's online backup API into a destination keyed with the
/// <em>same</em> DEK. The result is one self-contained, consistent, SQLCipher-encrypted <c>.db</c> file (WAL
/// frames included, no <c>-wal</c>/<c>-shm</c> side-files) — the same vault Sprint 24's restore swaps in. It
/// does not conflict with the app's pooled connections and needs no checkpoint or pool flush.</item>
/// <item><b>Byte copy</b> (fallback, only when no DEK is held): copies the encrypted bytes of the main file and
/// any <c>-wal</c>/<c>-shm</c>, opened with <see cref="FileShare.ReadWrite"/> | <see cref="FileShare.Delete"/>
/// so it tolerates other open handles. Paths that use it: the restore swap and its pre-restore safety copy
/// (they run at startup before login, against a closed database), and any snapshot attempted while the vault
/// is locked (e.g. a daily snapshot that fires after auto-lock cleared the DEK).</item>
/// </list>
/// Pre-migration backups run after login (the migration itself needs the DEK), so they take the online path.
/// </summary>
internal static class BackupSnapshotWriter
{
    private const string TempSuffix = ".tmp";
    private static readonly string[] _sideFileSuffixes = ["-wal", "-shm", "-journal"];

    /// <summary>
    /// Writes a consistent snapshot of <paramref name="source"/> to <paramref name="destination"/> atomically:
    /// everything goes to <c>destination.tmp</c> first and is moved into place only on success (the main file
    /// last, so its presence implies the whole set landed). Stale side-files at the destination are removed
    /// when the new snapshot has none. With a <paramref name="dek"/> the online backup API is used; without one
    /// the byte-copy fallback. The caller owns <paramref name="dek"/> (and zeroes it afterwards).
    /// </summary>
    public static async Task<SnapshotMethod> WriteSnapshotAsync(
        string source,
        string destination,
        byte[]? dek,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var tmp = destination + TempSuffix;
        DeleteSet(tmp);
        try
        {
            SnapshotMethod method;
            if (dek is not null)
            {
                await BackupDatabaseAsync(source, tmp, dek, ct).ConfigureAwait(false);
                method = SnapshotMethod.OnlineBackup;
            }
            else
            {
                await CopyDatabaseAsync(source, tmp, ct).ConfigureAwait(false);
                method = SnapshotMethod.ByteCopy;
            }

            MoveOrDropStale(tmp + "-wal", destination + "-wal");
            MoveOrDropStale(tmp + "-shm", destination + "-shm");
            File.Move(tmp, destination, overwrite: true);
            return method;
        }
        catch
        {
            TryDeleteSet(tmp);
            throw;
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/> into <paramref name="destination"/> with SQLite's online backup API,
    /// both connections keyed with <paramref name="dek"/>, then integrity-checks the destination. Runs off the
    /// calling thread (the backup step is synchronous). Both connections are non-pooled, so no handle outlives
    /// the call and the destination can be moved or deleted immediately afterwards.
    /// </summary>
    public static Task BackupDatabaseAsync(string source, string destination, byte[] dek, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dek);
        return Task.Run(() => BackupDatabase(source, destination, dek, ct), ct);
    }

    /// <summary>
    /// Byte-copy fallback: copies <paramref name="source"/> (+ its <c>-wal</c>/<c>-shm</c>) to
    /// <paramref name="destination"/>, overwriting. Only for paths where no DEK is held (see the type remarks).
    /// </summary>
    public static async Task CopyDatabaseAsync(string source, string destination, CancellationToken ct)
    {
        await CopyFileAsync(source, destination, ct).ConfigureAwait(false);
        await CopySideFileIfPresentAsync(source + "-wal", destination + "-wal", ct).ConfigureAwait(false);
        await CopySideFileIfPresentAsync(source + "-shm", destination + "-shm", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A copy of the held DEK, or <c>null</c> when the vault is locked (or is locked concurrently). The caller
    /// zeroes the returned array once the snapshot is written.
    /// </summary>
    public static byte[]? TryGetDek(IDekHolder dekHolder)
    {
        ArgumentNullException.ThrowIfNull(dekHolder);
        if (!dekHolder.IsAvailable)
        {
            return null;
        }

        try
        {
            return dekHolder.Get();
        }
        catch (InvalidOperationException)
        {
            // Cleared between the check and the read (logout / auto-lock racing the backup).
            return null;
        }
    }

    /// <summary>Zeroes a DEK copy obtained from <see cref="TryGetDek"/>; a no-op for <c>null</c>.</summary>
    public static void Zero(byte[]? dek)
    {
        if (dek is not null)
        {
            Array.Clear(dek, 0, dek.Length);
        }
    }

    private static void BackupDatabase(string source, string destination, byte[] dek, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("The database to back up does not exist.", source);
        }

        // A leftover file at the destination would be opened (and keyed) as an existing database.
        DeleteSet(destination);

        using (var sourceConnection = OpenKeyed(source, SqliteOpenMode.ReadWrite, dek))
        using (var destinationConnection = OpenKeyed(destination, SqliteOpenMode.ReadWriteCreate, dek))
        {
            sourceConnection.BackupDatabase(destinationConnection);
            VerifyIntegrity(destinationConnection);
        }

        // Non-pooled connections are fully closed by Dispose; a rollback journal the destination used while
        // being written is gone by now, but drop any stray one so the snapshot is a single file.
        DeleteIfExists(destination + "-journal");
    }

    private static SqliteConnection OpenKeyed(string path, SqliteOpenMode mode, byte[] dek)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            // Never pooled: a pooled backup connection would keep the snapshot file (or the live database)
            // open after the backup and block the move into place, the restore swap, or deletion.
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            SqlCipherKeyInterceptor.KeyConnection(connection, dek);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void VerifyIntegrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = command.ExecuteScalar() as string;
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The backup snapshot failed its integrity check.");
        }
    }

    private static async Task CopySideFileIfPresentAsync(string source, string destination, CancellationToken ct)
    {
        if (File.Exists(source))
        {
            await CopyFileAsync(source, destination, ct).ConfigureAwait(false);
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken ct)
    {
        // ReadWrite | Delete: SQLite (and any pooled connection) holds the file open for writing, so a
        // read-only share mode would fail with a sharing violation.
        await using var sourceStream = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        await using var destinationStream = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await sourceStream.CopyToAsync(destinationStream, ct).ConfigureAwait(false);
    }

    private static void MoveOrDropStale(string source, string destination)
    {
        if (File.Exists(source))
        {
            File.Move(source, destination, overwrite: true);
        }
        else
        {
            // No side-file in this snapshot — a stale one would be replayed over the new main file.
            DeleteIfExists(destination);
        }
    }

    private static void DeleteSet(string path)
    {
        DeleteIfExists(path);
        foreach (var suffix in _sideFileSuffixes)
        {
            DeleteIfExists(path + suffix);
        }
    }

    private static void TryDeleteSet(string path)
    {
        try
        {
            DeleteSet(path);
        }
        catch (IOException)
        {
            // Best effort while unwinding a failed snapshot — the original exception is what matters, and the
            // next attempt deletes the leftover temp set before it starts.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale.
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

/// <summary>Which strategy <see cref="BackupSnapshotWriter"/> used for a snapshot (for logging).</summary>
internal enum SnapshotMethod
{
    OnlineBackup,
    ByteCopy,
}
