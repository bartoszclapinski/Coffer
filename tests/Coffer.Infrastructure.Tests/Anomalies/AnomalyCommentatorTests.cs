using Coffer.Core.Ai;
using Coffer.Core.Anomalies;
using Coffer.Infrastructure.AI;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Anomalies;

/// <summary>
/// The 13-B AI commentary, exercised with a scripted fake provider (no real API calls). Covers: the
/// prompt is anonymised before it leaves the process (hard rule #7), the call is metered exactly
/// once as <c>anomaly-comment</c>, a denied budget gate keeps the templated text without calling the
/// provider, and any provider/JSON failure falls back to the templated text.
/// </summary>
public class AnomalyCommentatorTests
{
    private static readonly DateOnly _day = new(2026, 6, 10);

    [Fact]
    public async Task Comment_RewritesTitleAndDescription_AndMetersOnceAsAnomalyComment()
    {
        var provider = new ScriptedProvider(
            """[{"index":0,"title":"Nowy tytuł","description":"Nowy opis."}]""");
        var ledger = new RecordingLedger();
        var commentator = Commentator(provider, ledger: ledger);

        var result = await commentator.CommentAsync([Candidate("high-amount:1")], CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Title.Should().Be("Nowy tytuł");
        result[0].Description.Should().Be("Nowy opis.");
        ledger.Records.Should().ContainSingle();
        ledger.Records[0].Purpose.Should().Be(AiPurpose.AnomalyComment);
    }

    [Fact]
    public async Task Comment_AnonymisesPromptBeforeSending()
    {
        var provider = new ScriptedProvider("[]");
        var commentator = Commentator(provider, anonymizer: new MarkingAnonymizer());

        await commentator.CommentAsync([Candidate("high-amount:1")], CancellationToken.None);

        provider.Requests.Should().ContainSingle();
        provider.Requests[0].Prompt.Should().StartWith("ANON:", "the prompt is anonymised (hard rule #7)");
    }

    [Fact]
    public async Task Comment_BudgetDenied_KeepsTemplatedTextWithoutCallingProvider()
    {
        var provider = new ScriptedProvider("[]");
        var ledger = new RecordingLedger();
        var commentator = Commentator(provider, gate: new StubBudgetGate(canProceed: false), ledger: ledger);

        var result = await commentator.CommentAsync([Candidate("high-amount:1")], CancellationToken.None);

        result[0].Title.Should().Be("Szablonowy tytuł");
        provider.Requests.Should().BeEmpty("a blocked budget gate never calls the provider");
        ledger.Records.Should().BeEmpty("a blocked call is never metered");
    }

    [Fact]
    public async Task Comment_ProviderThrows_FallsBackToTemplatedText()
    {
        var provider = new ScriptedProvider(throws: true);
        var commentator = Commentator(provider);

        var result = await commentator.CommentAsync([Candidate("high-amount:1")], CancellationToken.None);

        result[0].Title.Should().Be("Szablonowy tytuł");
        result[0].Description.Should().Be("Szablonowy opis.");
    }

    [Fact]
    public async Task Comment_PartialResponse_KeepsTemplatedTextForMissingIndexes()
    {
        var provider = new ScriptedProvider(
            """[{"index":0,"title":"Tylko pierwszy","description":"Opis."}]""");
        var commentator = Commentator(provider);

        var result = await commentator.CommentAsync(
            [Candidate("high-amount:1"), Candidate("new-merchant:x")], CancellationToken.None);

        result[0].Title.Should().Be("Tylko pierwszy");
        result[1].Title.Should().Be("Szablonowy tytuł", "a missing index keeps the templated text");
    }

    [Fact]
    public async Task Comment_NoCandidates_ReturnsEmptyWithoutCallingProvider()
    {
        var provider = new ScriptedProvider("[]");
        var commentator = Commentator(provider);

        var result = await commentator.CommentAsync([], CancellationToken.None);

        result.Should().BeEmpty();
        provider.Requests.Should().BeEmpty();
    }

    private static AnomalyCommentator Commentator(
        ScriptedProvider provider,
        IAiBudgetGate? gate = null,
        IAiUsageLedger? ledger = null,
        IPromptAnonymizer? anonymizer = null) =>
        new(
            provider,
            gate ?? new StubBudgetGate(canProceed: true),
            ledger ?? new RecordingLedger(),
            new StubPricing(),
            anonymizer ?? new PassthroughAnonymizer(),
            NullLogger<AnomalyCommentator>.Instance);

    private static AnomalyCandidate Candidate(string signature) =>
        new(
            AnomalyType.HighAmountInCategory,
            5.0,
            signature,
            "Szablonowy tytuł",
            "Szablonowy opis.",
            Guid.NewGuid(),
            500m,
            _day,
            _day,
            new Dictionary<string, string> { ["amount"] = "500" });

    private sealed class ScriptedProvider : IAiProvider
    {
        private readonly string? _json;
        private readonly bool _throws;

        public ScriptedProvider(string json) => _json = json;

        public ScriptedProvider(bool throws) => _throws = throws;

        public string ProviderName => "Scripted";
        public List<AiRequest> Requests { get; } = [];

        public Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            if (_throws)
            {
                throw new InvalidOperationException("provider boom");
            }

            var value = System.Text.Json.JsonSerializer.Deserialize<TResult>(
                _json!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            var usage = new AiUsage(ProviderName, request.Model, 100, 20);
            return Task.FromResult(new AiResult<TResult>(value, usage));
        }

        public Task<AiResult<string>> CompleteAsync(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AiResult<AiToolTurn>> CompleteWithToolsAsync(AiToolRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class StubBudgetGate : IAiBudgetGate
    {
        private readonly bool _canProceed;

        public StubBudgetGate(bool canProceed) => _canProceed = canProceed;

        public Task<bool> CanProceedAsync(decimal estimatedCostPln, AiPriority priority, CancellationToken ct) =>
            Task.FromResult(_canProceed);
    }

    private sealed class RecordingLedger : IAiUsageLedger
    {
        public List<(AiUsage Usage, string Purpose)> Records { get; } = [];

        public Task RecordAsync(AiUsage usage, string purpose, CancellationToken ct)
        {
            Records.Add((usage, purpose));
            return Task.CompletedTask;
        }

        public Task<decimal> GetCurrentMonthSpendPlnAsync(CancellationToken ct) => Task.FromResult(0m);

        public Task<IReadOnlyList<AiSpendByPurpose>> GetCurrentMonthByPurposeAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AiSpendByPurpose>>([]);
    }

    private sealed class StubPricing : IAiPricing
    {
        public AiCost Estimate(string model, int inputTokens, int outputTokens) => new(0.01m, 0.04m);
    }

    private sealed class PassthroughAnonymizer : IPromptAnonymizer
    {
        public string Anonymize(string text) => text;
    }

    private sealed class MarkingAnonymizer : IPromptAnonymizer
    {
        public string Anonymize(string text) => "ANON:" + text;
    }
}
