using System.Text;
using Coffer.Core.Parsing;
using Coffer.Shared.Parsing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Coffer.Infrastructure.Parsing;

/// <summary>
/// Format-aware bank detector. For <see cref="StatementFormat.Pdf"/> it matches
/// a small set of known bank phrases against the first page's text; for
/// <see cref="StatementFormat.Csv"/> it matches the PKO BP "Historia rachunku"
/// header signature. Adding a new PDF bank is a one-line entry in
/// <see cref="_fingerprints"/>. PdfPig stays an Infrastructure-only dependency
/// (hard rule #3): the interface in <c>Coffer.Core</c> only knows
/// <see cref="StatementInput"/>.
/// </summary>
public sealed class FingerprintBankDetector : IBankDetector
{
    private static readonly Encoding _windows1250;

    static FingerprintBankDetector()
    {
        // The CSV branch reads Windows-1250; registering the provider is
        // idempotent and safe to call more than once. Register before resolving
        // the encoding (field initialisers run first otherwise).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _windows1250 = Encoding.GetEncoding(1250);
    }

    /// <summary>
    /// Order matters only when two fingerprints could match the same document;
    /// the highest <c>Priority</c> wins. All entries use Priority 1 because none
    /// of them overlap in practice.
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

    public BankFingerprint? Detect(StatementInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.Format switch
        {
            StatementFormat.Pdf => DetectPdf(input),
            StatementFormat.Csv => DetectCsv(input),
            _ => null,
        };
    }

    private static BankFingerprint? DetectPdf(StatementInput input)
    {
        var text = ReadFirstPageText(input);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return _fingerprints
            .Where(f => text.Contains(f.BankName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Priority)
            .FirstOrDefault();
    }

    private static string? ReadFirstPageText(StatementInput input)
    {
        input.Content.Position = 0;
        using var buffer = new MemoryStream();
        input.Content.CopyTo(buffer);
        input.Content.Position = 0;

        try
        {
            using var document = PdfDocument.Open(buffer.ToArray());
            if (document.NumberOfPages == 0)
            {
                return null;
            }

            Page firstPage = document.GetPage(1);
            return firstPage.Text;
        }
        catch (Exception)
        {
            // A malformed / scanned PDF renders the document undetectable rather
            // than crashing the import flow.
            return null;
        }
    }

    private static BankFingerprint? DetectCsv(StatementInput input)
    {
        input.Content.Position = 0;
        string? headerLine;
        using (var reader = new StreamReader(
                   input.Content, _windows1250, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
        {
            do
            {
                headerLine = reader.ReadLine();
            }
            while (headerLine is not null && string.IsNullOrWhiteSpace(headerLine));
        }
        input.Content.Position = 0;

        if (headerLine is null)
        {
            return null;
        }

        // PKO "Historia rachunku" signature: the first row names these columns.
        var isPkoHistoria =
            headerLine.Contains("Data operacji", StringComparison.OrdinalIgnoreCase) &&
            headerLine.Contains("Saldo po transakcji", StringComparison.OrdinalIgnoreCase) &&
            headerLine.Contains("Opis transakcji", StringComparison.OrdinalIgnoreCase);

        return isPkoHistoria
            ? _fingerprints.First(f => f.BankCode == "PKO_BP")
            : null;
    }
}
