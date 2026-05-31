using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Coffer.Infrastructure.Tests.Parsing;

/// <summary>
/// Builds tiny in-memory PDFs containing free-form text on a single page. Used by
/// <see cref="FingerprintBankDetectorTests"/> to feed the detector recognisable
/// bank-name phrases without committing real-shape statements.
/// </summary>
internal static class SyntheticTextPdfBuilder
{
    static SyntheticTextPdfBuilder()
    {
        // QuestPDF's free community licence — declared once per process. Setting it
        // here means the value is in place before any test instantiates a builder.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static MemoryStream Build(string text)
    {
        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(12));
                page.Content().Text(text);
            });
        }).GeneratePdf();

        return new MemoryStream(bytes);
    }
}
