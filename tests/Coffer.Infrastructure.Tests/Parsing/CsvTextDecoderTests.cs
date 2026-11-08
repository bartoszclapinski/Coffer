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
