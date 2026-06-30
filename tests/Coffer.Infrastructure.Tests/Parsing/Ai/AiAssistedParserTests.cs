using System.Text;
using System.Text.Json;
using Coffer.Core.Ai;
using Coffer.Core.Parsing;
using Coffer.Core.Security;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Parsing.Ai;
using Coffer.Shared.Parsing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Parsing.Ai;

public class AiAssistedParserTests
{
    private const string TwoTransactionsJson =
        """
        {"currency":"PLN","periodFrom":"2026-01-01","periodTo":"2026-01-31","transactions":[
          {"date":"2026-01-05","bookingDate":"2026-01-06","amount":-49.99,"currency":"PLN","description":"BIEDRONKA 1234","merchant":"BIEDRONKA"},
          {"date":"2026-01-10","bookingDate":null,"amount":5000.00,"currency":"PLN","description":"WYNAGRODZENIE","merchant":null}
        ]}
        """;

    [Fact]
    public async Task ParseAsync_Enabled_MapsJsonToMediumConfidenceResult()
    {
        var provider = new FakeAiProvider(TwoTransactionsJson);
        var parser = Build(provider);

        var result = await parser.ParseAsync(CsvInput("anything"), CancellationToken.None);

        result.BankCode.Should().Be(AiAssistedParser.AiFallbackBankCode);
        result.Confidence.Should().Be(ParserConfidence.Medium);
        result.AccountNumber.Should().BeEmpty();
        result.Currency.Should().Be("PLN");
        result.PeriodFrom.Should().Be(new DateOnly(2026, 1, 1));
        result.PeriodTo.Should().Be(new DateOnly(2026, 1, 31));
        result.Transactions.Should().HaveCount(2);
        result.Transactions[0].Amount.Should().Be(-49.99m);
        result.Transactions[0].Merchant.Should().Be("BIEDRONKA");
        result.Transactions[1].BookingDate.Should().BeNull();
        result.Warnings.Should().Contain(AiAssistedParser.ReviewWarning);
        result.Warnings.Should().Contain(AiAssistedParser.AccountNumberAbsentWarning);
    }

    [Fact]
    public async Task ParseAsync_OptInDisabled_ThrowsUnsupportedBank()
    {
        var parser = Build(new FakeAiProvider(TwoTransactionsJson), enabled: false);

        var act = () => parser.ParseAsync(CsvInput("anything"), CancellationToken.None);

        await act.Should().ThrowAsync<UnsupportedBankException>();
    }

    [Fact]
    public async Task ParseAsync_NoApiKey_ThrowsUnsupportedBank()
    {
        var parser = Build(new FakeAiProvider(TwoTransactionsJson), apiKey: null);

        var act = () => parser.ParseAsync(CsvInput("anything"), CancellationToken.None);

        await act.Should().ThrowAsync<UnsupportedBankException>();
    }

    [Fact]
    public async Task ParseAsync_BudgetDenied_ThrowsUnsupportedBank()
    {
        var parser = Build(new FakeAiProvider(TwoTransactionsJson), budgetAllows: false);

        var act = () => parser.ParseAsync(CsvInput("anything"), CancellationToken.None);

        await act.Should().ThrowAsync<UnsupportedBankException>();
    }

    [Fact]
    public async Task ParseAsync_RecordsOneParserFallbackLedgerEntry()
    {
        var ledger = new RecordingLedger();
        var parser = Build(new FakeAiProvider(TwoTransactionsJson), ledger: ledger);

        await parser.ParseAsync(CsvInput("anything"), CancellationToken.None);

        ledger.Purposes.Should().ContainSingle().Which.Should().Be(AiPurpose.ParserFallback);
    }

    [Fact]
    public async Task ParseAsync_OwnerNameSet_RedactsNameInPrompt()
    {
        var provider = new FakeAiProvider(TwoTransactionsJson);
        var parser = Build(provider, ownerNames: "Jan Kowalski");

        var result = await parser.ParseAsync(CsvInput("Posiadacz: Jan Kowalski"), CancellationToken.None);

        provider.LastPrompt.Should().Contain("[NAME]");
        provider.LastPrompt.Should().NotContain("Jan Kowalski");
        result.Warnings.Should().NotContain(AiAssistedParser.OwnerNameUnsetWarning);
    }

    [Fact]
    public async Task ParseAsync_OwnerNameUnset_AddsHeaderExposureWarning()
    {
        var parser = Build(new FakeAiProvider(TwoTransactionsJson), ownerNames: null);

        var result = await parser.ParseAsync(CsvInput("anything"), CancellationToken.None);

        result.Warnings.Should().Contain(AiAssistedParser.OwnerNameUnsetWarning);
    }

    [Fact]
    public async Task ParseAsync_MalformedRow_IsSkippedNotFatal()
    {
        const string json =
            """
            {"currency":"PLN","transactions":[
              {"date":"not-a-date","amount":-10.00,"description":"bad"},
              {"date":"2026-01-10","amount":5000.00,"description":"good"}
            ]}
            """;
        var parser = Build(new FakeAiProvider(json));

        var result = await parser.ParseAsync(CsvInput("anything"), CancellationToken.None);

        result.Transactions.Should().ContainSingle();
        result.Transactions[0].Description.Should().Be("good");
        result.Warnings.Should().Contain(w => w.Contains("could not be parsed"));
    }

    private static AiAssistedParser Build(
        FakeAiProvider provider,
        bool enabled = true,
        string? apiKey = "sk-test",
        bool budgetAllows = true,
        string? ownerNames = null,
        IAiUsageLedger? ledger = null)
    {
        var settings = new FakeSettings { Enabled = enabled, OwnerNames = ownerNames };
        var secrets = new FakeSecretStore(apiKey);
        return new AiAssistedParser(
            provider,
            new FakeBudgetGate(budgetAllows),
            ledger ?? new RecordingLedger(),
            new FakePricing(),
            new PromptAnonymizer(),
            settings,
            secrets,
            NullLogger<AiAssistedParser>.Instance);
    }

    private static StatementInput CsvInput(string text) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(text)), StatementFormat.Csv);

    private sealed class FakeAiProvider(string json) : IAiProvider
    {
        private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

        public string? LastPrompt { get; private set; }

        public string ProviderName => "Fake";

        public Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct)
        {
            LastPrompt = request.Prompt;
            var value = JsonSerializer.Deserialize<TResult>(json, _options)!;
            return Task.FromResult(new AiResult<TResult>(value, new AiUsage("Fake", request.Model, 100, 50)));
        }

        public Task<AiResult<string>> CompleteAsync(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AiResult<AiToolTurn>> CompleteWithToolsAsync(AiToolRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeBudgetGate(bool allows) : IAiBudgetGate
    {
        public Task<bool> CanProceedAsync(decimal estimatedCostPln, AiPriority priority, CancellationToken ct) =>
            Task.FromResult(allows);
    }

    private sealed class RecordingLedger : IAiUsageLedger
    {
        public List<string> Purposes { get; } = [];

        public Task RecordAsync(AiUsage usage, string purpose, CancellationToken ct)
        {
            Purposes.Add(purpose);
            return Task.CompletedTask;
        }

        public Task<decimal> GetCurrentMonthSpendPlnAsync(CancellationToken ct) => Task.FromResult(0m);

        public Task<IReadOnlyList<AiSpendByPurpose>> GetCurrentMonthByPurposeAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AiSpendByPurpose>>([]);
    }

    private sealed class FakePricing : IAiPricing
    {
        public AiCost Estimate(string model, int inputTokens, int outputTokens) => new(0.01m, 0.04m);
    }

    private sealed class FakeSecretStore(string? key) : ISecretStore
    {
        public Task<string?> GetSecretAsync(string name, CancellationToken ct) => Task.FromResult(key);

        public Task SetSecretAsync(string name, string value, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteSecretAsync(string name, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeSettings : IAiSettings
    {
        public bool Enabled { get; set; }

        public string? OwnerNames { get; set; }

        public Task<bool> GetAiFallbackParsingEnabledAsync(CancellationToken ct) => Task.FromResult(Enabled);

        public Task SetAiFallbackParsingEnabledAsync(bool enabled, CancellationToken ct) => Task.CompletedTask;

        public Task<string?> GetOwnerIdentityNamesAsync(CancellationToken ct) => Task.FromResult(OwnerNames);

        public Task SetOwnerIdentityNamesAsync(string? names, CancellationToken ct) => Task.CompletedTask;

        public Task<decimal> GetMonthlyCapPlnAsync(CancellationToken ct) => Task.FromResult(AiDefaults.MonthlyCapPln);

        public Task SetMonthlyCapPlnAsync(decimal capPln, CancellationToken ct) => Task.CompletedTask;

        public Task<string> GetActiveProviderAsync(CancellationToken ct) => Task.FromResult(AiDefaults.ClaudeProvider);

        public Task SetActiveProviderAsync(string provider, CancellationToken ct) => Task.CompletedTask;

        public Task<string> GetCategorizationModelAsync(CancellationToken ct) =>
            Task.FromResult(AiDefaults.CategorizationModel);

        public Task SetCategorizationModelAsync(string model, CancellationToken ct) => Task.CompletedTask;
    }
}
