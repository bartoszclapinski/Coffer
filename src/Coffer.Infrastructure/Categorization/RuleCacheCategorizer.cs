using Coffer.Core.Categorization;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Categorization;

/// <summary>
/// Deterministic categoriser (Phase 10-A): for each distinct normalised description,
/// resolve <b>cache → rules</b>. A rule hit is written back to the cache
/// (<see cref="CacheSource.Rule"/>) so the next encounter is a free cache hit. Anything
/// neither cached nor matched by a rule maps to <c>null</c> — left for the Phase 10-C AI stage.
/// Loads rules and the relevant cache rows in one round-trip and batches the write-back.
/// </summary>
public sealed class RuleCacheCategorizer : ICategorizer
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;
    private readonly ICategoryRuleEngine _ruleEngine;

    public RuleCacheCategorizer(
        IDbContextFactory<CofferDbContext> contextFactory, ICategoryRuleEngine ruleEngine)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(ruleEngine);

        _contextFactory = contextFactory;
        _ruleEngine = ruleEngine;
    }

    public async Task<IReadOnlyDictionary<string, Guid?>> CategorizeAsync(
        IReadOnlyCollection<string> normalizedDescriptions, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(normalizedDescriptions);

        var keys = normalizedDescriptions
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var result = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        if (keys.Count == 0)
        {
            return result;
        }

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var cached = await db.CategoryCache
            .Where(c => keys.Contains(c.NormalizedDescription))
            .ToDictionaryAsync(c => c.NormalizedDescription, c => c, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

        var rules = await db.Rules.AsNoTracking()
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var now = DateTime.UtcNow;
        foreach (var key in keys)
        {
            if (cached.TryGetValue(key, out var hit))
            {
                hit.HitCount++;
                hit.LastUsedAt = now;
                result[key] = hit.CategoryId;
                continue;
            }

            var ruleMatch = _ruleEngine.Match(key, rules);
            result[key] = ruleMatch;
            if (ruleMatch is { } categoryId)
            {
                db.CategoryCache.Add(new CategoryCache
                {
                    Id = Guid.NewGuid(),
                    NormalizedDescription = key,
                    CategoryId = categoryId,
                    Source = CacheSource.Rule,
                    HitCount = 1,
                    LastUsedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return result;
    }
}
