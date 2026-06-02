namespace Coffer.Core.Domain;

/// <summary>
/// Minimal category for the transaction-list filter. Auto-categorisation (rules,
/// AI) arrives in Phase 4; for now transactions stay uncategorised and this exists
/// only so the list has something to bind a filter to.
/// </summary>
public class Category
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Color { get; set; } = "#534AB7";

    public bool IsArchived { get; set; }
}
