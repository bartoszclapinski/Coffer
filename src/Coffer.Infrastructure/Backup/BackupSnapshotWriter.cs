namespace Coffer.Infrastructure.Backup;

/// <summary>
/// Shared file-copy for backups: copies the SQLCipher database and its <c>-wal</c>/<c>-shm</c> side-files
/// to a destination path. Copying the encrypted bytes preserves at-rest protection; copying the side-files
/// alongside the main file keeps the set internally consistent even without a forced checkpoint. Both the
/// pre-migration hook and the daily snapshot service use this one path.
/// </summary>
internal static class BackupSnapshotWriter
{
    /// <summary>Copies <paramref name="source"/> (+ its side-files) to <paramref name="destination"/>, overwriting.</summary>
    public static async Task CopyDatabaseAsync(string source, string destination, CancellationToken ct)
    {
        await CopyFileAsync(source, destination, ct).ConfigureAwait(false);
        await CopySideFileIfPresentAsync(source + "-wal", destination + "-wal", ct).ConfigureAwait(false);
        await CopySideFileIfPresentAsync(source + "-shm", destination + "-shm", ct).ConfigureAwait(false);
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
        await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destinationStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await sourceStream.CopyToAsync(destinationStream, ct).ConfigureAwait(false);
    }
}
