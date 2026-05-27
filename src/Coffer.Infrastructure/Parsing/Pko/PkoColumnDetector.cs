using Coffer.Infrastructure.Parsing.Polish;
using UglyToad.PdfPig.Content;

namespace Coffer.Infrastructure.Parsing.Pko;

/// <summary>
/// Finds the transaction-table header row and computes each column's
/// <c>[start, end)</c> X range from the position of the labels. Each column
/// runs from the left edge of its label to the left edge of the next label
/// (or +infinity for the last column).
/// </summary>
internal static class PkoColumnDetector
{
    private const string _dateLabel = "Data";
    private const string _descriptionLabel = "Opis";
    private const string _debitLabel = "Obciążenia";
    private const string _creditLabel = "Uznania";

    public static PkoColumnAnchors? FindHeaderRow(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        foreach (var row in PdfLetterGrouping.GroupIntoRows(page.Letters))
        {
            var rowText = row.FullText();
            if (!ContainsAll(rowText, _dateLabel, _descriptionLabel))
            {
                continue;
            }

            var dateStart = FindLabelStart(row, _dateLabel);
            var descriptionStart = FindLabelStart(row, _descriptionLabel);
            var debitStart = FindLabelStart(row, _debitLabel);
            var creditStart = FindLabelStart(row, _creditLabel);

            if (dateStart is null || descriptionStart is null)
            {
                continue;
            }

            return new PkoColumnAnchors(
                Date: (dateStart.Value, descriptionStart.Value),
                Description: (descriptionStart.Value, debitStart ?? double.PositiveInfinity),
                Debit: debitStart is not null
                    ? (debitStart.Value, creditStart ?? double.PositiveInfinity)
                    : (double.NaN, double.NaN),
                Credit: creditStart is not null
                    ? (creditStart.Value, double.PositiveInfinity)
                    : (double.NaN, double.NaN),
                HeaderRowY: row[0].Location.Y);
        }

        return null;
    }

    private static bool ContainsAll(string source, params string[] tokens) =>
        tokens.All(t => source.Contains(t, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// X coordinate of the leftmost letter of <paramref name="label"/> within
    /// the row. Returns <c>null</c> when the label is not present.
    /// </summary>
    private static double? FindLabelStart(IReadOnlyList<Letter> row, string label)
    {
        var rowText = row.FullText();
        var index = rowText.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || index >= row.Count)
        {
            return null;
        }
        return row[index].Location.X;
    }
}
