using UglyToad.PdfPig.Content;

namespace Coffer.Infrastructure.Parsing.Polish;

/// <summary>
/// Groups PdfPig <see cref="Letter"/>s into rows by Y coordinate proximity. A
/// row is "a set of letters with similar Y" — within <c>yTolerance</c> of one
/// another's Y position. Sorted top-to-bottom; within each row, sorted
/// left-to-right.
/// </summary>
/// <remarks>
/// PdfPig coordinates are PDF-native: Y increases upward. This implementation
/// sorts by descending Y so the first row returned is the topmost row on the
/// page — natural reading order.
/// </remarks>
public static class PdfLetterGrouping
{
    public static IEnumerable<IReadOnlyList<Letter>> GroupIntoRows(
        IReadOnlyList<Letter> letters,
        double yTolerance = 2.0)
    {
        ArgumentNullException.ThrowIfNull(letters);
        if (yTolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(yTolerance), yTolerance, "Tolerance must be positive.");
        }

        if (letters.Count == 0)
        {
            yield break;
        }

        var sorted = letters
            .OrderByDescending(l => l.Location.Y)
            .ThenBy(l => l.Location.X)
            .ToList();

        var current = new List<Letter>();
        double? anchorY = null;

        foreach (var letter in sorted)
        {
            if (anchorY is null || Math.Abs(letter.Location.Y - anchorY.Value) <= yTolerance)
            {
                current.Add(letter);
                anchorY ??= letter.Location.Y;
            }
            else
            {
                yield return SortRowLeftToRight(current);
                current = [letter];
                anchorY = letter.Location.Y;
            }
        }

        if (current.Count > 0)
        {
            yield return SortRowLeftToRight(current);
        }
    }

    private static IReadOnlyList<Letter> SortRowLeftToRight(List<Letter> row) =>
        row.OrderBy(l => l.Location.X).ToList();
}
