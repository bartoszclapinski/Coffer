using Coffer.Core.Categorization;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Categorization;

/// <summary>
/// EF-backed <see cref="ICategoryCacheStore"/>. Exact lookup by the unique
/// <c>NormalizedDescription</c>; upsert bumps reuse counters and applies source
/// precedence so a manual correction is never silently undone by a later rule/AI write.
/// </summary>
public sealed class CategoryCacheStore : ICategoryCacheStore
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public CategoryCacheStore(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<Guid?> GetAsync(string normalizedDescription, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(normalizedDescription))
        {
            return null;
        }

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entry = await db.CategoryCache
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.NormalizedDescription == normalizedDescription, ct)
            .ConfigureAwait(false);
        return entry?.CategoryId;
    }

    public async Task UpsertAsync(
        string normalizedDescription, Guid categoryId, CacheSource source, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(normalizedDescription))
        {
            return;
        }

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.CategoryCache
            .FirstOrDefaultAsync(c => c.NormalizedDescription == normalizedDescription, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.CategoryCache.Add(new CategoryCache
            {
                Id = Guid.NewGuid(),
                NormalizedDescription = normalizedDescription,
                CategoryId = categoryId,
                Source = source,
                HitCount = 1,
                LastUsedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.HitCount++;
            existing.LastUsedAt = DateTime.UtcNow;

            // Source precedence (Manual > AI > Rule): a stronger or equal source may
            // re-point the category; a weaker source only refreshes the counters.
            if (source >= existing.Source)
            {
                existing.CategoryId = categoryId;
                existing.Source = source;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
