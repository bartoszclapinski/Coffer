using Coffer.Core.Domain;

namespace Coffer.Core.Categorization;

/// <summary>
/// The learned <c>NormalizedDescription → category</c> cache (the cost-control layer
/// of the hybrid pipeline). Exact-key lookup; upsert bumps reuse counters and honours
/// source precedence: a <see cref="CacheSource.Manual"/> write overrides any existing
/// entry, while a rule/AI write does not clobber a manual correction.
/// </summary>
public interface ICategoryCacheStore
{
    /// <summary>Returns the cached category for an exact normalised description, or null.</summary>
    Task<Guid?> GetAsync(string normalizedDescription, CancellationToken ct);

    /// <summary>
    /// Records or refreshes the mapping. An existing entry has its
    /// <c>HitCount</c>/<c>LastUsedAt</c> bumped; its category/source change only when the
    /// incoming <paramref name="source"/> is allowed to override the stored one
    /// (Manual &gt; AI/Rule).
    /// </summary>
    Task UpsertAsync(string normalizedDescription, Guid categoryId, CacheSource source, CancellationToken ct);
}
