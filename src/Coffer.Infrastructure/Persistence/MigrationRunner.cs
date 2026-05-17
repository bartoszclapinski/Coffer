using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Persistence;

public sealed class MigrationRunner
{
    private readonly CofferDbContext _db;
    private readonly ILogger<MigrationRunner> _logger;
    private readonly Func<CancellationToken, Task>? _preMigrationBackup;
    private readonly Func<string> _appVersionProvider;

    public MigrationRunner(
        CofferDbContext db,
        ILogger<MigrationRunner> logger,
        Func<CancellationToken, Task>? preMigrationBackup = null,
        Func<string>? appVersionProvider = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        _db = db;
        _logger = logger;
        _preMigrationBackup = preMigrationBackup;
        _appVersionProvider = appVersionProvider ?? ResolveAppVersionFromEntryAssembly;
    }

    public async Task<MigrationResult> RunPendingMigrationsAsync(CancellationToken ct)
    {
        var pending = (await _db.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).ToList();
        if (pending.Count == 0)
        {
            _logger.LogInformation("No pending migrations");
            return MigrationResult.UpToDate();
        }

        _logger.LogInformation(
            "Running {Count} pending migration(s): {Migrations}",
            pending.Count,
            string.Join(", ", pending));

        if (_preMigrationBackup is not null)
        {
            // Backup failure must abort the migration — let the exception propagate.
            // The caller decides whether to retry; we do NOT touch the database without
            // a fresh backup attempt (hard rule #8).
            await _preMigrationBackup(ct).ConfigureAwait(false);
        }

        var appliedBefore = (await _db.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false))
            .ToHashSet();

        try
        {
            await _db.Database.MigrateAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation flows through; status is not surfaced as Failed.
            throw;
        }
        catch (Exception ex)
        {
            var appliedAfterFailure = (await _db.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false))
                .Where(m => !appliedBefore.Contains(m))
                .ToList();
            _logger.LogError(
                ex,
                "Migration failed after applying {Count} migration(s): {Migrations}",
                appliedAfterFailure.Count,
                string.Join(", ", appliedAfterFailure));
            return MigrationResult.Failed(appliedAfterFailure);
        }

        var newlyApplied = (await _db.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false))
            .Where(m => !appliedBefore.Contains(m))
            .ToList();

        _db.SchemaInfo.Add(new SchemaInfoEntry
        {
            Version = newlyApplied[^1],
            MigratedAt = DateTime.UtcNow,
            AppVersion = _appVersionProvider(),
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return MigrationResult.Migrated(newlyApplied);
    }

    private static string ResolveAppVersionFromEntryAssembly() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";
}
