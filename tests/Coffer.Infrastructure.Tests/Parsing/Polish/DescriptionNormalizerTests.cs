using Coffer.Infrastructure.Parsing.Polish;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Parsing.Polish;

public class DescriptionNormalizerTests
{
    [Theory]
    [InlineData("BIEDRONKA 1234", "BIEDRONKA 1234")]
    [InlineData("biedronka 1234", "BIEDRONKA 1234")]
    [InlineData("BIEDRONKA   1234", "BIEDRONKA 1234")]
    [InlineData("BIEDRONKA /PL/", "BIEDRONKA")]
    [InlineData("BLIK Biedronka", "BIEDRONKA")]
    [InlineData("KRD MPK Kraków", "MPK KRAKÓW")]
    [InlineData("Płatność kartą Lidl 7", "LIDL 7")]
    [InlineData("Lidl 7 **4321", "LIDL 7")]
    [InlineData("Allegro/****4321/", "ALLEGRO")]
    [InlineData("", "")]
    public void Normalize_TableDriven_MatchesExpected(string raw, string expected)
    {
        var result = DescriptionNormalizer.Normalize(raw);

        result.Should().Be(expected);
    }

    [Fact]
    public void Normalize_PreservesPolishDiacritics()
    {
        var result = DescriptionNormalizer.Normalize("kawiarnia żółć");

        // ToUpperInvariant on Polish characters keeps the diacritics.
        result.Should().Be("KAWIARNIA ŻÓŁĆ");
    }
}
