using Coffer.Infrastructure.Parsing.Pko;
using Coffer.Shared.Parsing;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Parsing.Pko;

public class PkoHistoriaCsvParserTests
{
    private static readonly PkoHistoriaCsvParser _parser = new();

    private static Task<ParseResult> ParseAsync(StatementInput input) =>
        _parser.ParseAsync(input, CancellationToken.None);

    [Fact]
    public async Task Parse_GoldenCsv_ReturnsAllTransactions()
    {
        var result = await ParseAsync(CsvStatementInputFactory.FromGoldenFile());

        result.BankCode.Should().Be("PKO_BP");
        result.Currency.Should().Be("PLN");
        result.Confidence.Should().Be(ParserConfidence.High);
        result.Transactions.Should().HaveCount(8);
        result.PeriodFrom.Should().Be(new DateOnly(2026, 1, 5));
        result.PeriodTo.Should().Be(new DateOnly(2026, 1, 20));

        var salary = result.Transactions[0];
        salary.Date.Should().Be(new DateOnly(2026, 1, 5));
        salary.Amount.Should().Be(5000.00m);
        salary.Currency.Should().Be("PLN");
        salary.Merchant.Should().Be("ACME SP Z OO");

        var transfer = result.Transactions[3];
        transfer.Amount.Should().Be(-1200.00m);
        transfer.Merchant.Should().Be("JAN KOWALSKI");
    }

    [Fact]
    public async Task Parse_DebitAndCredit_SignsCorrect()
    {
        var result = await ParseAsync(CsvStatementInputFactory.FromGoldenFile());

        result.Transactions.Count(t => t.Amount > 0).Should().Be(2);
        result.Transactions.Count(t => t.Amount < 0).Should().Be(6);
    }

    [Fact]
    public async Task Parse_EmbeddedCommaInQuotedField_NotSplit()
    {
        var result = await ParseAsync(CsvStatementInputFactory.FromGoldenFile());

        result.Transactions[1].Amount.Should().Be(-49.99m);
        result.Transactions[1].Description.Should().Contain("Zakupy spozywcze, napoje i przekaski");
    }

    [Fact]
    public async Task Parse_LeadingExcelGuard_Stripped()
    {
        var result = await ParseAsync(CsvStatementInputFactory.FromGoldenFile());

        var salary = result.Transactions[0];
        salary.Description.Should().NotContain("'");
        salary.Description.Should().Contain("11111111111111111111111111");
    }

    [Fact]
    public async Task Parse_RowWithoutMerchantLabel_MerchantNull()
    {
        var result = await ParseAsync(CsvStatementInputFactory.FromGoldenFile());

        // Row 3 (BLIK) carries only a "Tytul:" sub-field, no "Nazwa ...".
        result.Transactions[2].Merchant.Should().BeNull();
    }

    [Fact]
    public async Task Parse_AccountNumberAbsent_EmptyWithWarning()
    {
        var result = await ParseAsync(CsvStatementInputFactory.FromGoldenFile());

        result.AccountNumber.Should().BeEmpty();
        result.Warnings.Should().Contain(PkoHistoriaCsvParser.AccountNumberAbsentWarning);
    }

    [Fact]
    public async Task Parse_Windows1250_DecodesPolishDiacritics()
    {
        const string csv =
            "\"Data operacji\",\"Data waluty\",\"Typ transakcji\",\"Kwota\",\"Waluta\",\"Saldo po transakcji\",\"Opis transakcji\",\"\",\"\",\"\",\"\",\"\"\n" +
            "\"2026-01-05\",\"2026-01-05\",\"Przelew\",\"-12.34\",\"PLN\",\"100.00\",\"Przelew\",\"Nazwa nadawcy/odbiorcy: ŚWIADCZENIE ŁUBIANKA\",\"Tytul: Płatność\",\"\",\"\",\"\"\n";

        var result = await ParseAsync(CsvStatementInputFactory.FromCsv(csv));

        result.Transactions.Should().ContainSingle();
        result.Transactions[0].Merchant.Should().Be("ŚWIADCZENIE ŁUBIANKA");
        result.Transactions[0].Description.Should().Contain("Płatność");
    }

    [Fact]
    public async Task Parse_WrongHeaderShape_ThrowsUnsupportedCsvLayoutException()
    {
        const string csv =
            "\"Date\",\"Amount\",\"Description\"\n" +
            "\"2026-01-05\",\"-12.34\",\"Coffee\"\n";

        var act = async () => await ParseAsync(CsvStatementInputFactory.FromCsv(csv));

        await act.Should().ThrowAsync<UnsupportedCsvLayoutException>();
    }

    [Fact]
    public async Task Parse_EmptyFile_ThrowsUnsupportedCsvLayoutException()
    {
        var act = async () => await ParseAsync(CsvStatementInputFactory.FromCsv(string.Empty));

        await act.Should().ThrowAsync<UnsupportedCsvLayoutException>();
    }
}
