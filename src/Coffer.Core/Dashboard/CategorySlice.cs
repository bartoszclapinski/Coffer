namespace Coffer.Core.Dashboard;

/// <summary>
/// One slice of the spend-by-category breakdown. <see cref="Total"/> is the positive
/// spend magnitude in the period; <see cref="Share"/> is its fraction of the total
/// spend (0..1). <see cref="CategoryId"/> is null for the "uncategorised" slice, and
/// a synthesised remainder slice (everything past the top N) carries its own label.
/// </summary>
public sealed record CategorySlice(
    Guid? CategoryId,
    string Name,
    string Color,
    decimal Total,
    double Share);
