using Coffer.Core.Ai;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Persists one <see cref="AiUsageEntry"/> per AI call and answers month-to-date spend
/// questions (doc 04). Cost is priced through <see cref="IAiPricing"/> so the ledger and
/// the budget gate agree. "Current month" is the calendar month in UTC, matching the
/// app's UTC timestamp convention (hard rule #2).
/// </summary>
public sealed class AiUsageLedger : IAiUsageLedger
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;
    private readonly IAiPricing _pricing;

    public AiUsageLedger(IDbContextFactory<CofferDbContext> contextFactory, IAiPricing pricing)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(pricing);

        _contextFactory = contextFactory;
        _pricing = pricing;
    }

    public async Task RecordAsync(AiUsage usage, string purpose, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentException.ThrowIfNullOrEmpty(purpose);

        var cost = _pricing.Estimate(usage.Model, usage.InputTokens, usage.OutputTokens);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.AiUsageEntries.Add(new AiUsageEntry
        {
            Id = Guid.NewGuid(),
            At = DateTime.UtcNow,
            Provider = usage.Provider,
            Model = usage.Model,
            Purpose = purpose,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            EstimatedCostUsd = cost.Usd,
            EstimatedCostPln = cost.Pln,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<decimal> GetCurrentMonthSpendPlnAsync(CancellationToken ct)
    {
        var monthStart = CurrentMonthStartUtc();

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.AiUsageEntries
            .Where(e => e.At >= monthStart)
            .SumAsync(e => e.EstimatedCostPln, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AiSpendByPurpose>> GetCurrentMonthByPurposeAsync(CancellationToken ct)
    {
        var monthStart = CurrentMonthStartUtc();

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var rows = await db.AiUsageEntries
            .Where(e => e.At >= monthStart)
            .GroupBy(e => e.Purpose)
            .Select(g => new AiSpendByPurpose(g.Key, g.Sum(e => e.EstimatedCostPln)))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows;
    }

    private static DateTime CurrentMonthStartUtc()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
