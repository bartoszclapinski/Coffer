namespace Coffer.Core.Domain;

/// <summary>
/// A deterministic categorisation rule: when <see cref="Pattern"/> (a regex) matches
/// a transaction's <c>NormalizedDescription</c>, the transaction is assigned
/// <see cref="CategoryId"/>. Lower <see cref="Priority"/> wins; the first enabled
/// match in priority order decides. Seeded with a starter pack and editable by the
/// owner (Phase 4).
/// </summary>
public class Rule
{
    public Guid Id { get; set; }

    public int Priority { get; set; }

    public string Pattern { get; set; } = "";

    public Guid CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public bool IsEnabled { get; set; } = true;
}
