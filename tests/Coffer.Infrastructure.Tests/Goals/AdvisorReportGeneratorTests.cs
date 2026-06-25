using System.Globalization;
using Coffer.Core.Ai;
using Coffer.Core.Domain;
using Coffer.Core.Goals;
using Coffer.Infrastructure.AI;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Goals;

/// <summary>
/// The 14-C advisor report generator, exercised with a scripted fake provider (no real API calls).
/// Covers: the prompt is anonymised before it leaves the process (hard rule #7), the call is metered
/// exactly once as <c>advisor-report</c>, a denied budget gate or a provider/JSON failure falls back
/// to an engine-only report carrying the deterministic risk text, and the generator never fabricates
/// suggestion numbers when the LLM is unavailable.
/// </summary>
public class AdvisorReportGeneratorTests
{
    private static readonly DateOnly _day = new(2026, 6, 10);
    private static readonly Guid _goalId = Guid.NewGuid();

    [Fact]
    public async Task Generate_ProducesRisksAndSuggestions_AndMetersOnceAsAdvisorReport()
    {
        var json = string.Create(CultureInfo.InvariantCulture, $$"""
            {
              "perGoalRisks": { "{{_goalId}}": ["Napięty termin osiągnięcia celu."] },
              "suggestions": [
                { "title": "Restauracje do średniej", "savings": 329, "description": "Z 540 do 211 zł.", "categoryAffected": "Restauracje" }
              ]
            }
            """);
        var provider = new ScriptedProvider(json);
        var ledger = new RecordingLedger();
        var generator = Generator(provider, ledger: ledger);

        var report = await generator.GenerateAsync(
            [Result()], Ctx(), [Spend()], _day, CancellationToken.None);

        report.GeneratedByAi.Should().BeTrue();
        report.Date.Should().Be(_day);

        var risk = report.Entries.Single(e => e.Kind == AdvisorEntryKind.Risk);
        risk.GoalId.Should().Be(_goalId);
        risk.Description.Should().Be("Napięty termin osiągnięcia celu.");

        var suggestion = report.Entries.Single(e => e.Kind == AdvisorEntryKind.Suggestion);
        suggestion.Title.Should().Be("Restauracje do średniej");
        suggestion.Savings.Should().Be(329m);
        suggestion.CategoryAffected.Should().Be("Restauracje");

        ledger.Records.Should().ContainSingle();
        ledger.Records[0].Purpose.Should().Be(AiPurpose.AdvisorReport);
    }

    [Fact]
    public async Task Generate_AnonymisesPromptBeforeSending()
    {
        var provider = new ScriptedProvider("""{"perGoalRisks":{},"suggestions":[]}""");
        var generator = Generator(provider, anonymizer: new MarkingAnonymizer());

        await generator.GenerateAsync([Result()], Ctx(), [Spend()], _day, CancellationToken.None);

        provider.Requests.Should().ContainSingle();
        provider.Requests[0].Prompt.Should().StartWith("ANON:", "the prompt is anonymised (hard rule #7)");
    }

    [Fact]
    public async Task Generate_BudgetDenied_FallsBackToEngineOnly_WithoutCallingProvider()
    {
        var provider = new ScriptedProvider("""{"perGoalRisks":{},"suggestions":[]}""");
        var ledger = new RecordingLedger();
        var generator = Generator(provider, gate: new StubBudgetGate(canProceed: false), ledger: ledger);

        var report = await generator.GenerateAsync(
            [Result()], Ctx(), [Spend()], _day, CancellationToken.None);

        report.GeneratedByAi.Should().BeFalse();
        report.Entries.Should().ContainSingle();
        report.Entries[0].Kind.Should().Be(AdvisorEntryKind.Risk);
        report.Entries[0].Description.Should().Be("Napięty termin.");
        provider.Requests.Should().BeEmpty("a blocked budget gate never calls the provider");
        ledger.Records.Should().BeEmpty("a blocked call is never metered");
    }

    [Fact]
    public async Task Generate_ProviderThrows_FallsBackToEngineOnly()
    {
        var generator = Generator(new ScriptedProvider(throws: true));

        var report = await generator.GenerateAsync(
            [Result()], Ctx(), [Spend()], _day, CancellationToken.None);

        report.GeneratedByAi.Should().BeFalse();
        report.Entries.Should().Contain(e => e.Kind == AdvisorEntryKind.Risk && e.Description == "Napięty termin.");
    }

    [Fact]
    public async Task Generate_MalformedJson_FallsBackToEngineOnly()
    {
        var generator = Generator(new ScriptedProvider("{ this is not valid json"));

        var report = await generator.GenerateAsync(
            [Result()], Ctx(), [Spend()], _day, CancellationToken.None);

        report.GeneratedByAi.Should().BeFalse();
        report.Entries.Should().OnlyContain(e => e.Kind == AdvisorEntryKind.Risk);
    }

    [Fact]
    public async Task Generate_FallbackNeverFabricatesSuggestionNumbers()
    {
        var generator = Generator(new ScriptedProvider(throws: true));

        var report = await generator.GenerateAsync(
            [Result()], Ctx(), [Spend()], _day, CancellationToken.None);

        report.Entries.Should().NotContain(
            e => e.Kind == AdvisorEntryKind.Suggestion,
            "with no LLM the advisor must not invent any cutting suggestion or its savings");
    }

    [Fact]
    public async Task Generate_RisksForUnknownGoal_AreDropped()
    {
        var stranger = Guid.NewGuid();
        var json = string.Create(CultureInfo.InvariantCulture, $$"""
            {
              "perGoalRisks": { "{{stranger}}": ["Ryzyko dla nieznanego celu."] },
              "suggestions": []
            }
            """);
        var generator = Generator(new ScriptedProvider(json));

        var report = await generator.GenerateAsync(
            [Result()], Ctx(), [Spend()], _day, CancellationToken.None);

        report.GeneratedByAi.Should().BeTrue();
        report.Entries.Should().BeEmpty("a risk keyed by an unknown goal id is ignored");
    }

    [Fact]
    public async Task Generate_NoResults_ReturnsEmptyEngineReport_WithoutCallingProvider()
    {
        var provider = new ScriptedProvider("""{"perGoalRisks":{},"suggestions":[]}""");
        var generator = Generator(provider);

        var report = await generator.GenerateAsync([], Ctx(), [], _day, CancellationToken.None);

        report.GeneratedByAi.Should().BeFalse();
        report.Entries.Should().BeEmpty();
        provider.Requests.Should().BeEmpty();
    }

    private static AdvisorReportGenerator Generator(
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
            NullLogger<AdvisorReportGenerator>.Instance);

    private static GoalFeasibilityResult Result() => new()
    {
        GoalId = _goalId,
        Status = GoalStatus.AtRisk,
        EffectiveTarget = 8000m,
        ProjectedDate = new DateOnly(2027, 1, 1),
        RequiredMonthlySaving = 1000m,
        CurrentMonthlySaving = 400m,
        ConfidenceScore = 0.6m,
        AlternativeScenarios = [],
        Risks = [new RiskFactor("tight-timeline", "Napięty termin.")],
        DiagnosticSummary = "diag",
    };

    private static CategorySpending Spend() => new("Restauracje", 540m, 211m);

    private static FinancialContext Ctx() => new()
    {
        MonthlyIncome = 6000m,
        MonthlyFixedExpenses = 2500m,
        MonthlyVariableAvg = 1200m,
        MonthlyVariableStdDev = 150m,
        OtherActiveGoals = [],
        CategoryAverages6m = new Dictionary<string, decimal> { ["Restauracje"] = 211m },
        SeasonalityModifiers = new Dictionary<int, decimal>(),
        Today = _day,
    };

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
