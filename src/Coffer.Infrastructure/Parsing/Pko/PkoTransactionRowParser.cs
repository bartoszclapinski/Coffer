using Coffer.Infrastructure.Parsing.Polish;
using Coffer.Shared.Parsing;
using UglyToad.PdfPig.Content;

namespace Coffer.Infrastructure.Parsing.Pko;

/// <summary>
/// Classifies one row of letters against the column anchors and turns it into
/// a <see cref="ParsedTransaction"/>. Continuation rows (next row has no date
/// in the date column but has description-region text) are merged into the
/// preceding transaction by <c>PkoBpStatementParser</c>.
/// </summary>
internal static class PkoTransactionRowParser
{
    /// <summary>
    /// Returns a <see cref="ParsedTransaction"/> when the row carries a
    /// parseable date in the date column and an amount in either debit or
    /// credit column. Returns <c>null</c> for header / footer / continuation
    /// rows — the caller decides what to do with continuation rows.
    /// </summary>
    public static ParsedTransaction? TryParseRow(
        IReadOnlyList<Letter> row,
        PkoColumnAnchors anchors,
        string currency)
    {
        var dateText = TextInRange(row, anchors.Date);
        if (!PolishDateParser.TryParse(dateText, out var date))
        {
            return null;
        }

        var description = TextInRange(row, anchors.Description);

        decimal? debit = null;
        decimal? credit = null;
        if (!double.IsNaN(anchors.Debit.Start))
        {
            var debitText = TextInRange(row, anchors.Debit);
            if (PolishAmountParser.TryParse(debitText, out var debitValue) && debitValue != 0m)
            {
                debit = debitValue;
            }
        }
        if (!double.IsNaN(anchors.Credit.Start))
        {
            var creditText = TextInRange(row, anchors.Credit);
            if (PolishAmountParser.TryParse(creditText, out var creditValue) && creditValue != 0m)
            {
                credit = creditValue;
            }
        }

        // Row has a date but no amount in either column → continuation or divider.
        if (debit is null && credit is null)
        {
            return null;
        }

        // PKO statements print debits as positive numbers in the debit column;
        // signed convention (debit negative) lives here.
        var signedAmount = credit ?? -debit!.Value;

        return new ParsedTransaction(
            Date: date,
            BookingDate: null,
            Amount: signedAmount,
            Currency: currency,
            Description: description.Trim(),
            Merchant: null);
    }

    public static bool LooksLikeContinuation(
        IReadOnlyList<Letter> row,
        PkoColumnAnchors anchors)
    {
        var dateText = TextInRange(row, anchors.Date);
        if (PolishDateParser.TryParse(dateText, out _))
        {
            return false;
        }
        var descriptionText = TextInRange(row, anchors.Description);
        return !string.IsNullOrWhiteSpace(descriptionText);
    }

    public static string ExtractContinuationText(
        IReadOnlyList<Letter> row,
        PkoColumnAnchors anchors) =>
        TextInRange(row, anchors.Description).Trim();

    /// <summary>
    /// Concatenates letter values whose X falls inside <c>[range.Start, range.End)</c>.
    /// Half-open on the right so column N's letters never bleed into column N+1.
    /// </summary>
    private static string TextInRange(IReadOnlyList<Letter> row, (double Start, double End) range)
    {
        if (double.IsNaN(range.Start))
        {
            return string.Empty;
        }
        var span = row
            .Where(l => l.Location.X >= range.Start && l.Location.X < range.End)
            .Select(l => l.Value);
        return string.Concat(span).Trim();
    }
}
