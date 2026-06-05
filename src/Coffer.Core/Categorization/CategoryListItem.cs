namespace Coffer.Core.Categorization;

/// <summary>
/// A category projected for the UI picker / filter: id, display name, and colour
/// chip. Archived categories are excluded by the read side.
/// </summary>
public sealed record CategoryListItem(Guid Id, string Name, string Color);
