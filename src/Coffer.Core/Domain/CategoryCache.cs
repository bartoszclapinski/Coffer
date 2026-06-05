namespace Coffer.Core.Domain;

/// <summary>
/// A learned mapping from a transaction's <c>NormalizedDescription</c> to a category,
/// so a description already categorised once never costs a rule scan or an AI call
/// again. <see cref="NormalizedDescription"/> is unique. <see cref="Source"/> records
/// who decided it; a <see cref="CacheSource.Manual"/> entry overrides an earlier
/// rule/AI one. <see cref="HitCount"/> and <see cref="LastUsedAt"/> track reuse.
/// </summary>
public class CategoryCache
{
    public Guid Id { get; set; }

    public string NormalizedDescription { get; set; } = "";

    public Guid CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public CacheSource Source { get; set; }

    public int HitCount { get; set; }

    public DateTime LastUsedAt { get; set; }
}
