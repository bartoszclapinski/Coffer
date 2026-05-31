using UglyToad.PdfPig.Content;

namespace Coffer.Infrastructure.Parsing.Polish;

/// <summary>
/// Small extension helpers for working with a row produced by
/// <see cref="PdfLetterGrouping.GroupIntoRows"/>. Bank-specific parsers call
/// <see cref="TextAt"/> to slice columns by X position.
/// </summary>
public static class PdfRowExtensions
{
    /// <summary>
    /// Concatenates the <see cref="Letter.Value"/> of letters whose X centre
    /// falls inside <c>[xMin, xMax]</c>. The row must already be sorted
    /// left-to-right (which <see cref="PdfLetterGrouping.GroupIntoRows"/>
    /// guarantees).
    /// </summary>
    public static string TextAt(this IReadOnlyList<Letter> row, double xMin, double xMax)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (xMax < xMin)
        {
            throw new ArgumentException("xMax must be greater than or equal to xMin.", nameof(xMax));
        }

        var span = row
            .Where(l => l.Location.X >= xMin && l.Location.X <= xMax)
            .Select(l => l.Value);

        return string.Concat(span).Trim();
    }

    /// <summary>
    /// Full row text — every letter, left-to-right, joined with no separator.
    /// </summary>
    public static string FullText(this IReadOnlyList<Letter> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.Concat(row.Select(l => l.Value));
    }
}
