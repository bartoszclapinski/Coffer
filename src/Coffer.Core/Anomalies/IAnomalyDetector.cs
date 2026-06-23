namespace Coffer.Core.Anomalies;

/// <summary>
/// A single statistical detector (doc 04: "statistics first, AI second"). Pure and
/// synchronous — it reasons over the already-loaded <see cref="AnomalyDetectionContext"/>
/// and yields zero or more candidates. No I/O, no AI: detection is deterministic and free.
/// </summary>
public interface IAnomalyDetector
{
    IEnumerable<AnomalyCandidate> Detect(AnomalyDetectionContext context);
}
