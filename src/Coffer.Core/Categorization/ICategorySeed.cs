namespace Coffer.Core.Categorization;

/// <summary>
/// Seeds the opinionated default Polish category set and starter rule pack on first
/// run. Idempotent: categories are written only when none exist, rules only when none
/// exist — user edits are never overwritten.
/// </summary>
public interface ICategorySeed
{
    /// <summary>Returns the number of categories + rules inserted (0 when already seeded).</summary>
    Task<int> SeedAsync(CancellationToken ct);
}
