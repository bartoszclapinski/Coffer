using System.Text;
using Coffer.Infrastructure.Parsing;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Parsing;

public class CsvTextDecoderTests
{
    private const string PolishText = "Płatność kartą: ZAKŁAD ŚWIĘTOKRZYSKI, żółć";

    [Fact]
    public void Decode_Utf8WithBom_ReturnsTextWithoutBom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(PolishText)).ToArray();

        CsvTextDecoder.DetectEncoding(bytes).Should().Be(Encoding.UTF8);
        CsvTextDecoder.Decode(bytes).Should().Be(PolishText);
    }

    [Fact]
    public void Decode_Utf8WithoutBom_ReturnsText()
    {
        var bytes = Encoding.UTF8.GetBytes(PolishText);

        CsvTextDecoder.DetectEncoding(bytes).Should().Be(Encoding.UTF8);
        CsvTextDecoder.Decode(bytes).Should().Be(PolishText);
    }

    [Fact]
    public void Decode_Windows1250_FallsBackAndKeepsDiacritics()
    {
        var bytes = CsvTextDecoder.Windows1250.GetBytes(PolishText);

        CsvTextDecoder.DetectEncoding(bytes).Should().Be(CsvTextDecoder.Windows1250);
        CsvTextDecoder.Decode(bytes).Should().Be(PolishText);
    }

    [Theory]
    [InlineData("SPÓŁDZIELNIA MIESZKANIOWA", "SPӣDZIELNIA MIESZKANIOWA")]
    [InlineData("SPÓŁKA Z O.O.", "SPӣKA Z O.O.")]
    [InlineData("WIĘŹ SPÓŁDZIELCZA", "WIʏ SPӣDZIELCZA")]
    [InlineData("\"2026-01-05\",\"-10.00\",\"PLN\",\"SPÓŁKA ZWIĘŹLE\"", "\"2026-01-05\",\"-10.00\",\"PLN\",\"SPӣKA ZWIʏLE\"")]
    public void Decode_Windows1250ThatIsAlsoValidUtf8_FallsBackToWindows1250(string text, string asStrictUtf8)
    {
        // Upper-case Ó/Ć/Ę/Ń are UTF-8 lead bytes and Ł/Ż/Ź/Ś/Ą continuation bytes in
        // Windows-1250, so e.g. "SPÓŁ" (53 50 D3 A3) is strictly valid UTF-8 ("SPӣ").
        var bytes = CsvTextDecoder.Windows1250.GetBytes(text);
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        // Premise: strict UTF-8 validity alone would accept these bytes and garble them.
        strictUtf8.GetString(bytes).Should().Be(asStrictUtf8);

        CsvTextDecoder.DetectEncoding(bytes).Should().Be(CsvTextDecoder.Windows1250);
        CsvTextDecoder.Decode(bytes).Should().Be(text);
    }

    [Theory]
    [InlineData("Kwota 100,00 zł")] // NBSP
    [InlineData("„Cudzysłów” – opis…")] // General Punctuation
    [InlineData("Opłata 5 € za przelew")] // Currency Symbols
    [InlineData("Usługa™ nr № 7")] // Letterlike Symbols
    [InlineData("Straße Müller, Čeština ů")] // Latin-1 Supplement + Latin Extended-A
    public void Decode_Utf8WithPlausibleCharacters_StaysUtf8(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        CsvTextDecoder.DetectEncoding(bytes).Should().Be(Encoding.UTF8);
        CsvTextDecoder.Decode(bytes).Should().Be(text);
    }

    [Fact]
    public void Decode_PureAscii_IsUnchanged()
    {
        const string ascii = "\"Data operacji\",\"Kwota\"\r\n\"2026-01-05\",\"-10.00\"";

        CsvTextDecoder.Decode(Encoding.ASCII.GetBytes(ascii)).Should().Be(ascii);
    }

    [Fact]
    public void Decode_Empty_ReturnsEmpty()
    {
        CsvTextDecoder.Decode([]).Should().BeEmpty();
    }

    [Fact]
    public void ReadAllText_ReadsFromStartAndRewinds()
    {
        using var stream = new MemoryStream(CsvTextDecoder.Windows1250.GetBytes(PolishText));
        stream.Position = 5;

        var text = CsvTextDecoder.ReadAllText(stream);

        text.Should().Be(PolishText);
        stream.Position.Should().Be(0);
    }
}
