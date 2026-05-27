using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

namespace Coffer.Infrastructure.Tests.Parsing.Pko;

/// <summary>
/// Builds a synthetic PKO BP standard-checking statement PDF for CI. Layout is
/// "shape over fidelity" per Sprint 7 plan, open question #6 — column labels
/// match real PKO statements (so the parser's column detection works), but
/// numeric values are fully synthetic. The Anonymizer in Sprint 8 will replace
/// this with anonymized real-shape fixtures.
/// </summary>
internal sealed record SyntheticPkoTransaction(
    string Date,
    string Description,
    decimal? Debit,
    decimal? Credit);

internal sealed record SyntheticPkoStatement(
    string AccountNumber,
    string Currency,
    string PeriodFrom,
    string PeriodTo,
    IReadOnlyList<SyntheticPkoTransaction> Transactions);

internal static class SyntheticPkoPdfBuilder
{
    static SyntheticPkoPdfBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static PdfDocument Build(SyntheticPkoStatement statement)
    {
        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(column =>
                {
                    column.Spacing(8);

                    // Top-line bank identifier — recognised by FingerprintBankDetector.
                    column.Item().Text("PKO Bank Polski SA").Bold().FontSize(14);
                    column.Item().Text("Wyciąg z rachunku").FontSize(12);
                    column.Item().Text($"Numer rachunku: {statement.AccountNumber}");
                    column.Item().Text($"Waluta: {statement.Currency}");
                    column.Item().Text($"Okres: od {statement.PeriodFrom} do {statement.PeriodTo}");

                    column.Item().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);   // Data
                            c.RelativeColumn(6);   // Opis
                            c.RelativeColumn(2);   // Obciążenia
                            c.RelativeColumn(2);   // Uznania
                        });

                        table.Header(h =>
                        {
                            h.Cell().Text("Data");
                            h.Cell().Text("Opis");
                            h.Cell().Text("Obciążenia");
                            h.Cell().Text("Uznania");
                        });

                        foreach (var tx in statement.Transactions)
                        {
                            table.Cell().Text(tx.Date);
                            table.Cell().Text(tx.Description);
                            table.Cell().Text(tx.Debit is null ? string.Empty : Format(tx.Debit.Value));
                            table.Cell().Text(tx.Credit is null ? string.Empty : Format(tx.Credit.Value));
                        }
                    });
                });
            });
        }).GeneratePdf();

        return PdfDocument.Open(bytes);
    }

    private static string Format(decimal value) =>
        value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
}
