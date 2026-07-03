using Coffer.Core.Backup;

namespace Coffer.Application.Tests.Fakes;

/// <summary>In-memory <see cref="IBackupService"/>: serves a seeded status and records the snapshot calls.</summary>
internal sealed class FakeBackupService : IBackupService
{
    public BackupStatus Status { get; set; } = new(null, 0, null);

    public int DailyCalls { get; private set; }

    public int NowCalls { get; private set; }

    public Task<BackupResult> CreateDailySnapshotAsync(DateOnly today, CancellationToken ct)
    {
        DailyCalls++;
        return Task.FromResult(new BackupResult(true, "coffer-today.db"));
    }

    public Task<BackupResult> CreateSnapshotNowAsync(DateOnly today, CancellationToken ct)
    {
        NowCalls++;
        return Task.FromResult(new BackupResult(true, "coffer-today.db"));
    }

    public Task<BackupStatus> GetStatusAsync(CancellationToken ct) => Task.FromResult(Status);
}

/// <summary>In-memory <see cref="IArchiveExporter"/>: records the target path and can be made to throw.</summary>
internal sealed class FakeArchiveExporter : IArchiveExporter
{
    public int Calls { get; private set; }

    public string? LastTarget { get; private set; }

    public Exception? Throw { get; set; }

    public Task ExportAsync(string targetZipPath, CancellationToken ct)
    {
        Calls++;
        LastTarget = targetZipPath;
        return Throw is not null ? Task.FromException(Throw) : Task.CompletedTask;
    }
}
