using Coffer.Core.Parsing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Coffer.Infrastructure.Parsing;

/// <summary>
/// First-page text search against a small set of known bank phrases. Adding a
/// new bank is a one-line entry in <see cref="_fingerprints"/> — no DI churn.
/// Sprint 7 ships PKO BP with a parser plus six inert fingerprints: the
/// detector recognises them, but <c>StatementParserRegistry</c> throws
/// <see cref="UnsupportedBankException"/> until Sprint 8 swaps the throw for
/// the AI fallback parser.
/// </summary>
public sealed class FingerprintBankDetector : IBankDetector
{
    /// <summary>
    /// Order of <c>BankName</c> matters only when two fingerprints could match
    /// the same document; the highest <c>Priority</c> wins. All Sprint-7 entries
    /// use Priority 1 because none of them overlap in practice.
    /// </summary>
    private static readonly BankFingerprint[] _fingerprints =
    {
        new("PKO_BP",     "PKO Bank Polski",          1),
        new("ING",        "ING Bank Śląski",          1),
        new("MBANK",      "mBank S.A.",               1),
        new("PEKAO",      "Bank Polska Kasa Opieki",  1),
        new("SANTANDER",  "Santander Bank Polska",    1),
        new("MILLENNIUM", "Bank Millennium",          1),
        new("CITI",       "Citi Handlowy",            1),
        new("ALIOR",      "Alior Bank",               1),
    };

    public BankFingerprint? Detect(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.NumberOfPages == 0)
        {
            return null;
        }

        Page firstPage;
        try
        {
            firstPage = document.GetPage(1);
        }
        catch (Exception)
        {
            // Defensive — a malformed page renders the document undetectable
            // rather than crashing the whole import flow.
            return null;
        }

        var text = firstPage.Text;
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return _fingerprints
            .Where(f => text.Contains(f.BankName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Priority)
            .FirstOrDefault();
    }
}
