using Coffer.Infrastructure.AI;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

public class PromptAnonymizerTests
{
    private readonly PromptAnonymizer _anonymizer = new();

    [Fact]
    public void Anonymize_RedactsPolishIban()
    {
        var result = _anonymizer.Anonymize("Przelew na PL61 1090 1014 0000 0712 1981 2874 od Jana");

        result.Should().Contain("[IBAN]");
        result.Should().NotContain("1090 1014");
    }

    [Fact]
    public void Anonymize_RedactsBareAccountNumber()
    {
        var result = _anonymizer.Anonymize("Konto 82 1020 5604 0000 0102 8996 3017 zasilone");

        result.Should().Contain("[ACCOUNT]");
        result.Should().NotContain("1020 5604");
    }

    [Fact]
    public void Anonymize_RedactsNip()
    {
        var result = _anonymizer.Anonymize("Faktura NIP 123-456-78-90 za usługę");

        result.Should().Contain("[NIP]");
        result.Should().NotContain("123-456-78-90");
    }

    [Fact]
    public void Anonymize_PreservesMerchantNames()
    {
        const string text = "BIEDRONKA 1234 TORUN — zakupy spożywcze";

        var result = _anonymizer.Anonymize(text);

        result.Should().Contain("BIEDRONKA");
        result.Should().Contain("TORUN");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Anonymize_NullOrEmpty_ReturnsInput(string? text)
    {
        _anonymizer.Anonymize(text!).Should().Be(text);
    }

    [Fact]
    public void Anonymize_WithOwnerNames_RedactsName()
    {
        var result = _anonymizer.Anonymize("Posiadacz: Jan Kowalski, ul. Przykładowa 1", ["Jan Kowalski"]);

        result.Should().Contain("[NAME]");
        result.Should().NotContain("Jan Kowalski");
    }

    [Fact]
    public void Anonymize_WithOwnerNames_StillRedactsAccountAndKeepsMerchant()
    {
        var result = _anonymizer.Anonymize(
            "Jan Kowalski PL61 1090 1014 0000 0712 1981 2874 BIEDRONKA 1234",
            ["Jan Kowalski"]);

        result.Should().Contain("[NAME]");
        result.Should().Contain("[IBAN]");
        result.Should().Contain("BIEDRONKA");
    }

    [Fact]
    public void Anonymize_EmptyOwnerNames_BehavesLikeSingleArgument()
    {
        const string text = "Jan Kowalski PL61 1090 1014 0000 0712 1981 2874";

        var withEmpty = _anonymizer.Anonymize(text, []);
        var singleArg = _anonymizer.Anonymize(text);

        withEmpty.Should().Be(singleArg);
        withEmpty.Should().Contain("Jan Kowalski");
    }
}
