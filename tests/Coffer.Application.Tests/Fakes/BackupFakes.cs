using Coffer.Application.Dialogs;
using Coffer.Core.Backup;

namespace Coffer.Application.Tests.Fakes;

/// <summary>In-memory <see cref="IBackupService"/>: serves a seeded status and records the snapshot calls.</summary>
internal sealed class FakeBackupService : IBackupService
{
    public BackupStatus Status { get; set; } = new(null, 0, null);

    public int DailyCalls { get; private set; }

    public int NowCalls { get; private set; }

    /// <summary>When set, "Back up now" fails with this exception (after <see cref="StatusAfterFailure"/> applies).</summary>
    public Exception? NowThrow { get; set; }

    /// <summary>The status the service reports once a "Back up now" attempt has failed.</summary>
    public BackupStatus? StatusAfterFailure { get; set; }

    public Task<BackupResult> CreateDailySnapshotAsync(DateOnly today, CancellationToken ct)
    {
        DailyCalls++;
        return Task.FromResult(new BackupResult(true, "coffer-today.db"));
    }

    public Task<BackupResult> CreateSnapshotNowAsync(DateOnly today, CancellationToken ct)
    {
        NowCalls++;
        if (NowThrow is not null)
        {
            Status = StatusAfterFailure ?? Status;
            return Task.FromException<BackupResult>(NowThrow);
        }

        return Task.FromResult(new BackupResult(true, "coffer-today-manual.db"));
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

/// <summary>In-memory <see cref="IRestoreService"/>: serves seeded snapshots and records stage calls.</summary>
internal sealed class FakeRestoreService : IRestoreService
{
    public List<SnapshotInfo> Snapshots { get; } = [];

    public int StageCalls { get; private set; }

    public string? LastStagedFileName { get; private set; }

    public Exception? StageThrow { get; set; }

    public PendingRestore? Pending { get; set; }

    public Task<IReadOnlyList<SnapshotInfo>> ListSnapshotsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SnapshotInfo>>([.. Snapshots]);

    public Task<PendingRestore> StageRestoreAsync(string snapshotFileName, CancellationToken ct)
    {
        StageCalls++;
        LastStagedFileName = snapshotFileName;
        if (StageThrow is not null)
        {
            return Task.FromException<PendingRestore>(StageThrow);
        }

        var date = BackupRetention.ParseSnapshot(snapshotFileName)?.Date ?? DateOnly.MinValue;
        Pending = new PendingRestore(date, snapshotFileName, DateTime.UnixEpoch);
        return Task.FromResult(Pending);
    }

    public Task<PendingRestore?> GetPendingRestoreAsync(CancellationToken ct) => Task.FromResult(Pending);

    public Task CancelPendingRestoreAsync(CancellationToken ct)
    {
        Pending = null;
        return Task.CompletedTask;
    }

    public Task<RestoreResult> ApplyPendingRestoreAsync(CancellationToken ct)
    {
        var date = Pending?.SnapshotDate;
        Pending = null;
        return Task.FromResult(new RestoreResult(date is not null, date, null));
    }
}

/// <summary>In-memory <see cref="IRestoreDialogService"/>: records the open calls and returns a set result.</summary>
internal sealed class FakeRestoreDialogService : IRestoreDialogService
{
    public int ShowCalls { get; private set; }

    public bool ResultStaged { get; set; }

    public Task<bool> ShowRestoreDialogAsync(CancellationToken ct)
    {
        ShowCalls++;
        return Task.FromResult(ResultStaged);
    }
}
