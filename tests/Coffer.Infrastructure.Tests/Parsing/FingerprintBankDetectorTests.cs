using Coffer.Infrastructure.Parsing;
using Coffer.Infrastructure.Tests.Parsing.Pko;
using Coffer.Shared.Parsing;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Parsing;

public class FingerprintBankDetectorTests
{
    private static StatementInput Pdf(string firstPageText) =>
        new(SyntheticTextPdfBuilder.Build(firstPageText), StatementFormat.Pdf);

    /// <summary>
    /// For PDFs the detector reads the first page's text and matches against the
    /// fingerprint table. Tests construct a tiny synthetic PDF in memory that
    /// contains just the bank-name phrase on page one.
    /// </summary>
    [Theory]
    [InlineData("Wyciąg z rachunku PKO Bank Polski SA", "PKO_BP")]
    [InlineData("ING Bank Śląski - wyciąg miesięczny", "ING")]
    [InlineData("mBank S.A. statement", "MBANK")]
    [InlineData("Bank Millennium - zestawienie operacji", "MILLENNIUM")]
    [InlineData("Alior Bank - wyciąg z rachunku", "ALIOR")]
    public void Detect_KnownPdfBank_ReturnsExpectedFingerprint(string firstPageText, string expectedCode)
    {
        var detector = new FingerprintBankDetector();

        var result = detector.Detect(Pdf(firstPageText));

        result.Should().NotBeNull();
        result!.BankCode.Should().Be(expectedCode);
    }

    [Fact]
    public void Detect_UnknownPdfText_ReturnsNull()
    {
        var detector = new FingerprintBankDetector();

        var result = detector.Detect(Pdf("Some random statement without any known bank name"));

        result.Should().BeNull();
    }

    [Fact]
    public void Detect_LowerCasePdfBankName_StillMatches()
    {
        var detector = new FingerprintBankDetector();

        var result = detector.Detect(Pdf("wyciąg z konta - pko bank polski"));

        result.Should().NotBeNull();
        result!.BankCode.Should().Be("PKO_BP");
    }

    [Fact]
    public void Detect_PkoHistoriaCsvHeader_ReturnsPkoFingerprint()
    {
        var detector = new FingerprintBankDetector();
        var input = CsvStatementInputFactory.FromGoldenFile();

        var result = detector.Detect(input);

        result.Should().NotBeNull();
        result!.BankCode.Should().Be("PKO_BP");
    }

    [Fact]
    public void Detect_UnknownCsvHeader_ReturnsNull()
    {
        var detector = new FingerprintBankDetector();
        var input = CsvStatementInputFactory.FromCsv("\"Date\",\"Amount\",\"Memo\"\n\"2026-01-01\",\"1.00\",\"x\"\n");

        var result = detector.Detect(input);

        result.Should().BeNull();
    }
}
