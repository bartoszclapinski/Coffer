using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Persistence;

public sealed class MigrationRunner
{
    private readonly CofferDbContext _db;
    private readonly ILogger<MigrationRunner> _logger;
    private readonly Func<CancellationToken, Task>? _preMigrationBackup;

    public MigrationRunner(
        CofferDbContext db,
        ILogger<MigrationRunner> logger,
        Func<CancellationToken, Task>? preMigrationBackup = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        _db = db;
        _logger = logger;
        _preMigrationBackup = preMigrationBackup;
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
            await _preMigrationBackup(ct).ConfigureAwait(false);
        }

        await _db.Database.MigrateAsync(ct).ConfigureAwait(false);

        _db.SchemaInfo.Add(new SchemaInfoEntry
        {
            Version = pending[^1],
            MigratedAt = DateTime.UtcNow,
            AppVersion = ResolveAppVersion(),
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return MigrationResult.Migrated(pending);
    }

    private static string ResolveAppVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";
}
