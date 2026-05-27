using Coffer.Infrastructure.Parsing.Polish;
using FluentAssertions;
using FsCheck.Xunit;

namespace Coffer.Infrastructure.Tests.Parsing.Polish;

public class PolishDateParserTests
{
    [Theory]
    [InlineData("28.11.2025")]
    [InlineData("28-11-2025")]
    [InlineData("2025-11-28")]
    public void Parse_AllAcceptedFormats_RecoverSameDate(string raw)
    {
        var expected = new DateOnly(2025, 11, 28);

        var result = PolishDateParser.Parse(raw);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("31.02.2025")]    // 31 February — does not exist
    [InlineData("32.01.2025")]    // day 32
    [InlineData("01.13.2025")]    // month 13
    [InlineData("not a date")]
    [InlineData("")]
    public void Parse_RejectsInvalid_ThrowsFormatException(string raw)
    {
        var act = () => PolishDateParser.Parse(raw);

        act.Should().Throw<FormatException>();
    }

    [Property]
    public bool RoundTrip_ArbitraryDate_RecoversOriginal(DateTime dt)
    {
        var date = DateOnly.FromDateTime(dt);
        var formatted = date.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var parsed = PolishDateParser.Parse(formatted);
        return parsed == date;
    }
}
