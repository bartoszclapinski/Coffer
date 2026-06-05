namespace Coffer.Core.Ai;

/// <summary>Month-to-date AI spend for one purpose, for the Settings breakdown.</summary>
public sealed record AiSpendByPurpose(string Purpose, decimal SpendPln);

/// <summary>
/// Records every AI call's cost (doc 04) and answers month-to-date spend questions that
/// the budget gate and Settings page ask. Persists an <c>AiUsageEntry</c> row per call.
/// </summary>
public interface IAiUsageLedger
{
    Task RecordAsync(AiUsage usage, string purpose, CancellationToken ct);

    Task<decimal> GetCurrentMonthSpendPlnAsync(CancellationToken ct);

    Task<IReadOnlyList<AiSpendByPurpose>> GetCurrentMonthByPurposeAsync(CancellationToken ct);
}
