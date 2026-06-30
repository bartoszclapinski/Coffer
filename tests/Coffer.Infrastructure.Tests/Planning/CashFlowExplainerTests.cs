using Coffer.Core.Ai;
using Coffer.Core.Domain;
using Coffer.Core.Planning;
using Coffer.Infrastructure.Planning;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Planning;

/// <summary>
/// The 16-C cash-flow explainer, exercised with a scripted fake provider (no real API calls). Covers:
/// the prompt is anonymised before it leaves the process (hard rule #7); the call is metered exactly
/// once as <c>cashflow-explain</c>; a denied budget gate or a provider/empty-reply failure falls back
/// to a deterministic engine-only summary; and an empty projection never calls the provider.
/// </summary>
public class CashFlowExplainerTests
{
    private static readonly DateOnly _from = new(2026, 7, 1);

    [Fact]
    public async Task Explain_ProducesNarrative_AndMetersOnceAsCashFlowExplain()
    {
        var provider = new ScriptedProvider("10-tego wychodzi rata, 28-go wpływa pensja.");
        var ledger = new RecordingLedger();
        var explainer = Explainer(provider, ledger: ledger);

        var result = await explainer.ExplainAsync(Projection(), CancellationToken.None);

        result.GeneratedByAi.Should().BeTrue();
        result.Narrative.Should().Be("10-tego wychodzi rata, 28-go wpływa pensja.");
        ledger.Records.Should().ContainSingle();
        ledger.Records[0].Purpose.Should().Be(AiPurpose.CashFlowExplain);
    }

    [Fact]
    public async Task Explain_AnonymisesPromptBeforeSending()
    {
        var provider = new ScriptedProvider("ok");
        var explainer = Explainer(provider, anonymizer: new MarkingAnonymizer());

        await explainer.ExplainAsync(Projection(), CancellationToken.None);

        provider.Requests.Should().ContainSingle();
        provider.Requests[0].Prompt.Should().StartWith("ANON:", "the prompt is anonymised (hard rule #7)");
    }

    [Fact]
    public async Task Explain_BudgetDenied_FallsBackToEngineOnly_WithoutCallingProvider()
    {
        var provider = new ScriptedProvider("never used");
        var ledger = new RecordingLedger();
        var explainer = Explainer(provider, gate: new StubBudgetGate(canProceed: false), ledger: ledger);

        var result = await explainer.ExplainAsync(Projection(), CancellationToken.None);

        result.GeneratedByAi.Should().BeFalse();
        result.Narrative.Should().NotBeNullOrEmpty();
        provider.Requests.Should().BeEmpty("a blocked budget gate never calls the provider");
        ledger.Records.Should().BeEmpty("a blocked call is never metered");
    }

    [Fact]
    public async Task Explain_ProviderThrows_FallsBackToEngineOnly()
    {
        var explainer = Explainer(new ScriptedProvider(throws: true));

        var result = await explainer.ExplainAsync(Projection(), CancellationToken.None);

        result.GeneratedByAi.Should().BeFalse();
        result.Narrative.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Explain_EmptyReply_FallsBackToEngineOnly()
    {
        var explainer = Explainer(new ScriptedProvider("   "));

        var result = await explainer.ExplainAsync(Projection(), CancellationToken.None);

        result.GeneratedByAi.Should().BeFalse();
        result.Narrative.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Explain_NoEvents_ReturnsEngineOnly_WithoutCallingProvider()
    {
        var provider = new ScriptedProvider("never used");
        var explainer = Explainer(provider);
        var empty = new CashFlowProjectionEngine().Project([], 1000m, _from, 30);

        var result = await explainer.ExplainAsync(empty, CancellationToken.None);

        result.GeneratedByAi.Should().BeFalse();
        result.Narrative.Should().NotBeNullOrEmpty();
        provider.Requests.Should().BeEmpty();
    }

    private static CashFlowExplainer Explainer(
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
            NullLogger<CashFlowExplainer>.Instance);

    private static CashFlowProjection Projection() =>
        new CashFlowProjectionEngine().Project([Flow()], 1000m, _from, 60);

    private static RecurringFlow Flow() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Rata",
        Direction = FlowDirection.Outflow,
        IntervalMonths = 1,
        AnchorDayOfMonth = 10,
        TypicalAmount = 500m,
        Currency = "PLN",
        IsActive = true,
        Source = FlowSource.Manual,
        CreatedAt = DateTime.UtcNow,
    };

    private sealed class ScriptedProvider : IAiProvider
    {
        private readonly string? _text;
        private readonly bool _throws;

        public ScriptedProvider(string text) => _text = text;

        public ScriptedProvider(bool throws) => _throws = throws;

        public string ProviderName => "Scripted";

        public List<AiRequest> Requests { get; } = [];

        public Task<AiResult<string>> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            if (_throws)
            {
                throw new InvalidOperationException("provider boom");
            }

            var usage = new AiUsage(ProviderName, request.Model, 100, 20);
            return Task.FromResult(new AiResult<string>(_text!, usage));
        }

        public Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct) =>
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
