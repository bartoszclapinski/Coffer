namespace Coffer.Core.Anomalies;

/// <summary>
/// Runs every <see cref="IAnomalyDetector"/> over the current data and persists any new
/// findings as <see cref="Domain.Alert"/> rows. Idempotent: signatures already on record
/// (including dismissed ones) are skipped, so it is safe to call on every Alerty page open
/// and on a manual rescan. Returns the number of newly-persisted alerts.
/// </summary>
public interface IDetectAnomaliesUseCase
{
    Task<int> RunAsync(CancellationToken ct);
}
