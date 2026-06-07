namespace Coffer.Core.Ai;

/// <summary>
/// User-configurable AI settings, persisted in the encrypted DB. The monthly cap feeds
/// the budget gate; the active provider and categorisation model drive which vendor and
/// model categorisation uses. Sensible defaults apply until the owner changes them.
/// </summary>
public interface IAiSettings
{
    Task<decimal> GetMonthlyCapPlnAsync(CancellationToken ct);

    Task SetMonthlyCapPlnAsync(decimal capPln, CancellationToken ct);

    Task<string> GetActiveProviderAsync(CancellationToken ct);

    Task SetActiveProviderAsync(string provider, CancellationToken ct);

    Task<string> GetCategorizationModelAsync(CancellationToken ct);

    Task SetCategorizationModelAsync(string model, CancellationToken ct);
}
