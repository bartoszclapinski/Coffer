using Coffer.Infrastructure.Parsing.Polish;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Parsing.Polish;

public class AccountNumberNormalizerTests
{
    [Theory]
    [InlineData("PL61 1090 1014 0000 0712 1981 2874", "PL61109010140000071219812874")]
    [InlineData("PL61-1090-1014-0000-0712-1981-2874", "PL61109010140000071219812874")]
    [InlineData("pl61 1090 1014 0000 0712 1981 2874", "PL61109010140000071219812874")]
    [InlineData("PL611090101400000712 19812874", "PL61109010140000071219812874")]
    public void Normalize_VariantsConvergeOnSameForm(string raw, string expected)
    {
        var result = AccountNumberNormalizer.Normalize(raw);

        result.Should().Be(expected);
    }

    [Fact]
    public void Normalize_DomesticIbanWithoutCountryPrefix_AddsPL()
    {
        var domestic = "61 1090 1014 0000 0712 1981 2874"; // 26 digits, no PL prefix

        var result = AccountNumberNormalizer.Normalize(domestic);

        result.Should().Be("PL61109010140000071219812874");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_Empty_ReturnsEmpty(string? raw)
    {
        var result = AccountNumberNormalizer.Normalize(raw!);

        result.Should().BeEmpty();
    }
}
