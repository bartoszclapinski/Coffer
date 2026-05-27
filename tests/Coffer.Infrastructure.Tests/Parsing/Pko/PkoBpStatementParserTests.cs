using Coffer.Core.Parsing;
using Coffer.Infrastructure.Parsing.Pko;
using Coffer.Infrastructure.Tests.Parsing;
using Coffer.Shared.Parsing;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Parsing.Pko;

public class PkoBpStatementParserTests
{
    private static readonly SyntheticPkoStatement _sample = new(
        AccountNumber: "PL61 1090 1014 0000 0712 1981 2874",
        Currency: "PLN",
        PeriodFrom: "01.11.2025",
        PeriodTo: "30.11.2025",
        Transactions: new[]
        {
            new SyntheticPkoTransaction("02.11.2025", "BIEDRONKA 1234 KRAKOW", Debit: 49.99m, Credit: null),
            new SyntheticPkoTransaction("03.11.2025", "ORLEN STACJA 567", Debit: 240.50m, Credit: null),
            new SyntheticPkoTransaction("05.11.2025", "WYNAGRODZENIE PRACODAWCA", Debit: null, Credit: 7500.00m),
            new SyntheticPkoTransaction("08.11.2025", "MPK KRAKOW BILET", Debit: 6.20m, Credit: null),
            new SyntheticPkoTransaction("12.11.2025", "ZAKUP INTERNETOWY ALLEGRO", Debit: 159.00m, Credit: null),
            new SyntheticPkoTransaction("15.11.2025", "PRZELEW WLASNY", Debit: 1000.00m, Credit: null),
            new SyntheticPkoTransaction("18.11.2025", "LIDL 7", Debit: 87.34m, Credit: null),
            new SyntheticPkoTransaction("22.11.2025", "ZWROT POBRANIA", Debit: null, Credit: 35.00m),
            new SyntheticPkoTransaction("25.11.2025", "OPLATA ZA INTERNET", Debit: 65.00m, Credit: null),
            new SyntheticPkoTransaction("28.11.2025", "BIEDRONKA 1234 KRAKOW", Debit: 32.10m, Credit: null),
        });

    [Fact]
    public async Task Parse_SyntheticCheckingStatement_ReturnsAllTransactions()
    {
        using var pdf = SyntheticPkoPdfBuilder.Build(_sample);
        var parser = new PkoBpStatementParser();

        var result = await parser.ParseAsync(pdf, CancellationToken.None);

        result.BankCode.Should().Be("PKO_BP");
        result.Currency.Should().Be("PLN");
        result.Confidence.Should().Be(ParserConfidence.High);
        result.AccountNumber.Should().Be("PL61109010140000071219812874");
        result.PeriodFrom.Should().Be(new DateOnly(2025, 11, 1));
        result.PeriodTo.Should().Be(new DateOnly(2025, 11, 30));
        result.Transactions.Should().HaveCount(_sample.Transactions.Count);
    }

    [Fact]
    public async Task Parse_SyntheticCheckingStatement_DebitsAreNegative()
    {
        using var pdf = SyntheticPkoPdfBuilder.Build(_sample);
        var parser = new PkoBpStatementParser();

        var result = await parser.ParseAsync(pdf, CancellationToken.None);

        var biedronka = result.Transactions.First(t => t.Description.Contains("BIEDRONKA"));
        biedronka.Amount.Should().Be(-49.99m);
        biedronka.Currency.Should().Be("PLN");
    }

    [Fact]
    public async Task Parse_SyntheticCheckingStatement_CreditsAreSignedPositive()
    {
        using var pdf = SyntheticPkoPdfBuilder.Build(_sample);
        var parser = new PkoBpStatementParser();

        var result = await parser.ParseAsync(pdf, CancellationToken.None);

        var salary = result.Transactions.First(t => t.Description.Contains("WYNAGRODZENIE"));
        salary.Amount.Should().Be(7500.00m);
    }

    [Fact]
    public async Task Parse_CreditCardLayout_ThrowsUnsupportedPkoLayoutException()
    {
        using var pdf = SyntheticTextPdfBuilder.Build("PKO Bank Polski SA\nWyciąg z karty kredytowej\nGreat customer");
        var parser = new PkoBpStatementParser();

        var act = async () => await parser.ParseAsync(pdf, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<UnsupportedPkoLayoutException>();
        thrown.Which.LayoutHint.Should().Be("credit-card");
    }

    [Fact]
    public async Task Parse_SavingsLayout_ThrowsUnsupportedPkoLayoutException()
    {
        using var pdf = SyntheticTextPdfBuilder.Build("PKO Bank Polski SA\nWyciąg z konta oszczędnościowego");
        var parser = new PkoBpStatementParser();

        var act = async () => await parser.ParseAsync(pdf, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<UnsupportedPkoLayoutException>();
        thrown.Which.LayoutHint.Should().Be("savings");
    }

    [Fact]
    public void CanHandle_NonPkoFingerprint_ReturnsFalse()
    {
        var parser = new PkoBpStatementParser();

        parser.CanHandle(new BankFingerprint("MBANK", "mBank S.A.", 1)).Should().BeFalse();
        parser.CanHandle(new BankFingerprint("PKO_BP", "PKO Bank Polski", 1)).Should().BeTrue();
    }
}
