namespace Coffer.Infrastructure.Parsing.Pko;

/// <summary>
/// Column boundaries — exclusive ranges <c>[start, end)</c> in PDF X units —
/// for each column of the standard checking transaction table. Boundaries are
/// learned at runtime from the position of the header labels: each column
/// starts at the left edge of its own label and ends at the left edge of the
/// next column's label (or <see cref="double.PositiveInfinity"/> for the last
/// column).
/// </summary>
internal sealed record PkoColumnAnchors(
    (double Start, double End) Date,
    (double Start, double End) Description,
    (double Start, double End) Debit,
    (double Start, double End) Credit,
    double HeaderRowY);
