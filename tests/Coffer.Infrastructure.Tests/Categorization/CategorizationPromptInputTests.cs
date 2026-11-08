using Coffer.Infrastructure.Categorization;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Categorization;

public class CategorizationPromptInputTests
{
    [Fact]
    public void Build_Transfer_KeepsOnlyTheTitle()
    {
        var input = CategorizationPromptInput.Build(
            "RACHUNEK ODBIORCY: 11 1111 1111 1111 1111 1111 1111 NAZWA ODBIORCY: JAN TESTOWY "
            + "ADRES ODBIORCY: UL. FIKCYJNA 1 TESTOWO TYTUŁ: CZYNSZ LUTY");

        input.Should().Be("TYTUŁ: CZYNSZ LUTY");
    }

    [Fact]
    public void Build_CardPayment_KeepsTitleAndLocationDropsMetadataAndCard()
    {
        var input = CategorizationPromptInput.Build(
            "Tytuł: 00000079887766554433221 Lokalizacja: Adres: BIEDRONKA 1234 Miasto: TORUN Kraj: POLSKA "
            + "Data wykonania operacji: 2026-02-05 Oryginalna kwota operacji: 49.99 Numer karty: 425125******1234");

        input.Should().Be("Tytuł: 00000079887766554433221 Adres: BIEDRONKA 1234 Miasto: TORUN Kraj: POLSKA");
    }

    [Fact]
    public void Build_KeepsUnlabelledLead()
    {
        CategorizationPromptInput.Build("PRZELEW WYCHODZACY NAZWA ODBIORCY: JAN TESTOWY TYTUL: ZWROT")
            .Should().Be("PRZELEW WYCHODZACY TYTUL: ZWROT");
    }

    [Theory]
    [InlineData("BIEDRONKA 1234 TORUN", "BIEDRONKA 1234 TORUN")]
    [InlineData("  ORLEN STACJA NR 7 ", "ORLEN STACJA NR 7")]
    [InlineData("", "")]
    public void Build_NoLabels_KeepsTheWholeDescription(string description, string expected)
    {
        CategorizationPromptInput.Build(description).Should().Be(expected);
    }
}
