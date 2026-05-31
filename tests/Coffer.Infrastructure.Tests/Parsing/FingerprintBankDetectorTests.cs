using Coffer.Infrastructure.Parsing;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Parsing;

public class FingerprintBankDetectorTests
{
    /// <summary>
    /// The detector reads the first page's text and matches against the
    /// fingerprint table. Tests construct a tiny synthetic PDF in memory that
    /// contains just the bank-name phrase on page one.
    /// </summary>
    [Theory]
    [InlineData("Wyciąg z rachunku PKO Bank Polski SA", "PKO_BP")]
    [InlineData("ING Bank Śląski - wyciąg miesięczny", "ING")]
    [InlineData("mBank S.A. statement", "MBANK")]
    [InlineData("Bank Millennium - zestawienie operacji", "MILLENNIUM")]
    [InlineData("Alior Bank - wyciąg z rachunku", "ALIOR")]
    public void Detect_KnownBank_ReturnsExpectedFingerprint(string firstPageText, string expectedCode)
    {
        using var pdf = SyntheticTextPdfBuilder.Build(firstPageText);

        var detector = new FingerprintBankDetector();
        var result = detector.Detect(pdf);

        result.Should().NotBeNull();
        result!.BankCode.Should().Be(expectedCode);
    }

    [Fact]
    public void Detect_UnknownText_ReturnsNull()
    {
        using var pdf = SyntheticTextPdfBuilder.Build("Some random statement without any known bank name");

        var detector = new FingerprintBankDetector();
        var result = detector.Detect(pdf);

        result.Should().BeNull();
    }

    [Fact]
    public void Detect_LowerCaseBankName_StillMatches()
    {
        using var pdf = SyntheticTextPdfBuilder.Build("wyciąg z konta - pko bank polski");

        var detector = new FingerprintBankDetector();
        var result = detector.Detect(pdf);

        result.Should().NotBeNull();
        result!.BankCode.Should().Be("PKO_BP");
    }
}
