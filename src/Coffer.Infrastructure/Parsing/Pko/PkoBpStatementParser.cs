using Coffer.Core.Parsing;
using Coffer.Infrastructure.Parsing.Polish;
using Coffer.Shared.Parsing;
using UglyToad.PdfPig;

namespace Coffer.Infrastructure.Parsing.Pko;

/// <summary>
/// Deterministic parser for PKO BP's standard checking statements
/// ("Wyciąg z rachunku"). Other PKO layouts (credit card, savings, foreign
/// currency) throw <see cref="UnsupportedPkoLayoutException"/>; Sprint 8 adds
/// them and removes the throws.
/// </summary>
public sealed class PkoBpStatementParser : IStatementParser
{
    private const string _standardCheckingMarker = "Wyciąg z rachunku";
    private const string _creditCardMarker = "Wyciąg z karty kredytowej";
    private const string _savingsMarker = "Wyciąg z konta oszczędnościowego";
    private const string _foreignCurrencyMarker = "Wyciąg z rachunku walutowego";

    public string BankCode => "PKO_BP";

    public bool CanHandle(BankFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        return fingerprint.BankCode == BankCode;
    }

    public Task<ParseResult> ParseAsync(PdfDocument document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ct.ThrowIfCancellationRequested();

        if (document.NumberOfPages == 0)
        {
            throw new InvalidOperationException("PKO BP statement has no pages.");
        }

        var firstPage = document.GetPage(1);
        var pageText = firstPage.Text ?? string.Empty;

        // 1. Layout gate — only standard checking goes through; others throw.
        EnsureStandardCheckingLayout(pageText);

        // 2. Header (account, currency, period).
        var header = PkoStandardCheckingHeader.Extract(firstPage);
        var warnings = new List<string>();
        if (header.PeriodFrom == default || header.PeriodTo == default)
        {
            warnings.Add("Statement period could not be extracted from the header.");
        }

        // 3. Column anchors — measured on whichever page first contains the
        // transaction-table header. Reused for every subsequent page.
        PkoColumnAnchors? anchors = null;
        foreach (var page in document.GetPages())
        {
            anchors = PkoColumnDetector.FindHeaderRow(page);
            if (anchors is not null)
            {
                break;
            }
        }
        if (anchors is null)
        {
            throw new InvalidOperationException(
                "PKO BP statement does not contain a recognisable transaction-table header.");
        }

        // 4. Walk pages, extract transactions + continuation rows.
        var transactions = new List<ParsedTransaction>();
        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            ExtractTransactionsFromPage(page, anchors, header.Currency, transactions);
        }

        return Task.FromResult(new ParseResult(
            BankCode: BankCode,
            AccountNumber: header.AccountNumber,
            Currency: header.Currency,
            PeriodFrom: header.PeriodFrom,
            PeriodTo: header.PeriodTo,
            Transactions: transactions,
            Confidence: ParserConfidence.High,
            Warnings: warnings));
    }

    private static void EnsureStandardCheckingLayout(string pageText)
    {
        if (pageText.Contains(_standardCheckingMarker, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (pageText.Contains(_creditCardMarker, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsupportedPkoLayoutException("credit-card");
        }
        if (pageText.Contains(_savingsMarker, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsupportedPkoLayoutException("savings");
        }
        if (pageText.Contains(_foreignCurrencyMarker, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsupportedPkoLayoutException("foreign-currency");
        }
        throw new UnsupportedPkoLayoutException("unknown");
    }

    private static void ExtractTransactionsFromPage(
        UglyToad.PdfPig.Content.Page page,
        PkoColumnAnchors anchors,
        string currency,
        List<ParsedTransaction> transactions)
    {
        ParsedTransaction? current = null;
        var continuationBuffer = new List<string>();

        foreach (var row in PdfLetterGrouping.GroupIntoRows(page.Letters))
        {
            // Skip the header row + anything above it on this page; transactions
            // live strictly below the header.
            if (row[0].Location.Y >= anchors.HeaderRowY)
            {
                continue;
            }

            var transaction = PkoTransactionRowParser.TryParseRow(row, anchors, currency);
            if (transaction is not null)
            {
                FlushPending(transactions, ref current, continuationBuffer);
                current = transaction;
                continue;
            }

            if (current is not null && PkoTransactionRowParser.LooksLikeContinuation(row, anchors))
            {
                var continuation = PkoTransactionRowParser.ExtractContinuationText(row, anchors);
                if (!string.IsNullOrEmpty(continuation))
                {
                    continuationBuffer.Add(continuation);
                }
            }
        }

        FlushPending(transactions, ref current, continuationBuffer);
    }

    private static void FlushPending(
        List<ParsedTransaction> transactions,
        ref ParsedTransaction? current,
        List<string> continuationBuffer)
    {
        if (current is null)
        {
            continuationBuffer.Clear();
            return;
        }

        if (continuationBuffer.Count > 0)
        {
            var combined = string.Join(' ', new[] { current.Description }.Concat(continuationBuffer))
                .Trim();
            current = current with { Description = combined };
            continuationBuffer.Clear();
        }

        transactions.Add(current);
        current = null;
    }
}
