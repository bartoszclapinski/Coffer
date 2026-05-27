using Coffer.Infrastructure.Parsing.Polish;
using FluentAssertions;
using FsCheck.Xunit;

namespace Coffer.Infrastructure.Tests.Parsing.Polish;

public class PolishAmountParserTests
{
    [Theory]
    [InlineData("1234,56", 1234.56)]
    [InlineData("1 234,56", 1234.56)]              // regular space as thousand separator
    [InlineData("1 234,56", 1234.56)]         // non-breaking space (the real PKO case)
    [InlineData("1 234,56 zł", 1234.56)]
    [InlineData("12,00 PLN", 12.00)]
    [InlineData("0,01", 0.01)]
    [InlineData("-89,90", -89.90)]
    [InlineData("89,90-", -89.90)]                 // trailing-minus variant
    public void Parse_KnownPositiveOrNegative_ReturnsExpected(string raw, double expected)
    {
        var result = PolishAmountParser.Parse(raw);

        result.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not a number")]
    [InlineData("12.34.56")]
    public void Parse_RejectsGarbage_ThrowsFormatException(string? raw)
    {
        var act = () => PolishAmountParser.Parse(raw!);

        act.Should().Throw<FormatException>();
    }

    [Property]
    public bool RoundTrip_ArbitraryDecimal_RecoversOriginal(decimal d)
    {
        // FsCheck synthesises arbitrary decimals; we format them Polish-style and
        // verify the parser recovers the same value. Bound the magnitude — beyond
        // ~10^9 decimals are outside what a statement could plausibly print.
        var clamped = Math.Round(Math.Clamp(d, -1_000_000_000m, 1_000_000_000m), 2);
        var polishFormatted = clamped.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
            .Replace('.', ',');
        var parsed = PolishAmountParser.Parse(polishFormatted);
        return parsed == clamped;
    }
}
