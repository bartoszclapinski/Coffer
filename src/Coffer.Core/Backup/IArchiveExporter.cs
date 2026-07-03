namespace Coffer.Core.Backup;

/// <summary>
/// Exports a portable disaster-recovery archive (doc 08, Layer 3): a <c>.zip</c> of the encrypted
/// database, the encrypted DEK, and a small manifest. The files are already encrypted, so the archive is
/// not double-encrypted — it is only useful with the master password or the BIP39 seed.
/// </summary>
public interface IArchiveExporter
{
    /// <summary>Writes the archive to <paramref name="targetZipPath"/>, overwriting any existing file.</summary>
    Task ExportAsync(string targetZipPath, CancellationToken ct);
}
