using Coffer.Core.Ai;
using Coffer.Core.Chat;
using Coffer.Infrastructure.Chat;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Chat;

/// <summary>
/// The chat orchestrator's tool-call loop, exercised with a scripted fake provider (no real API
/// calls) and fake tools. Covers: the loop executes the requested tool with the model's args and
/// feeds the (anonymised) result back; the loop terminates on a final answer; every model turn is
/// metered into the ledger with Purpose=chat; a denied budget gate blocks before any provider call;
/// a missing API key surfaces as a friendly turn; unknown tool names are reported back to the model;
/// and the iteration cap is enforced.
/// </summary>
public class ChatServiceTests
{
    [Fact]
    public async Task Ask_ToolCallThenFinal_ExecutesToolWithArgsAndReturnsAnswer()
    {
        var tool = new FakeTool("GetTotalSpent", """{"totalSpent":320.50,"currency":"PLN"}""");
        var provider = new ScriptedProvider();
        provider.Enqueue(AiToolTurn(("GetTotalSpent", """{"from":"2026-06-01","to":"2026-06-30"}""")));
        provider.Enqueue(FinalTurn("W czerwcu wydałeś 320,50 PLN."));

        var ledger = new RecordingLedger();
        var service = Service(provider, [tool], ledger: ledger);

        var turn = await service.AskAsync("Ile wydałem w czerwcu?", [], CancellationToken.None);

        turn.Answer.Should().Be("W czerwcu wydałeś 320,50 PLN.");
        turn.BudgetExceeded.Should().BeFalse();
        turn.MissingApiKey.Should().BeFalse();
        tool.ReceivedArgs.Should().ContainSingle().Which.Should().Be("""{"from":"2026-06-01","to":"2026-06-30"}""");
        turn.ToolTraces.Should().ContainSingle();
        turn.ToolTraces[0].ToolName.Should().Be("GetTotalSpent");
        turn.ToolTraces[0].ArgumentsJson.Should().Be("""{"from":"2026-06-01","to":"2026-06-30"}""");
    }

    [Fact]
    public async Task Ask_FeedsAnonymisedToolResultBackToProvider()
    {
        var tool = new FakeTool("GetTransactions", """{"transactions":[{"merchant":"ORLEN"}]}""");
        var provider = new ScriptedProvider();
        provider.Enqueue(AiToolTurn(("GetTransactions", "{}")));
        provider.Enqueue(FinalTurn("Gotowe."));

        var service = Service(provider, [tool], anonymizer: new MarkingAnonymizer());

        await service.AskAsync("Pokaż transakcje", [], CancellationToken.None);

        // Second provider call carries the tool output as a Tool message; assert it was anonymised.
        provider.Requests.Should().HaveCount(2);
        var toolMessage = provider.Requests[1].Messages.Single(m => m.Role == AiChatRole.Tool);
        var result = toolMessage.ToolResults.Should().ContainSingle().Subject;
        result.ResultJson.Should().StartWith("ANON:", "tool output is anonymised before returning to the model (hard rule #7)");
        result.ResultJson.Should().Contain("""{"transactions":[{"merchant":"ORLEN"}]}""");
    }

    [Fact]
    public async Task Ask_MetersEveryModelTurnIntoLedgerWithChatPurpose()
    {
        var tool = new FakeTool("GetTotalSpent", "{}");
        var provider = new ScriptedProvider();
        provider.Enqueue(AiToolTurn(("GetTotalSpent", "{}")));
        provider.Enqueue(FinalTurn("Gotowe."));

        var ledger = new RecordingLedger();
        var service = Service(provider, [tool], ledger: ledger);

        await service.AskAsync("pytanie", [], CancellationToken.None);

        ledger.Records.Should().HaveCount(2, "both the tool-call turn and the final answer turn are billed");
        ledger.Records.Should().OnlyContain(r => r.Purpose == AiPurpose.Chat);
    }

    [Fact]
    public async Task Ask_BudgetDenied_BlocksBeforeAnyProviderCall()
    {
        var provider = new ScriptedProvider();
        var ledger = new RecordingLedger();
        var service = Service(provider, [], gate: new StubBudgetGate(canProceed: false), ledger: ledger);

        var turn = await service.AskAsync("pytanie", [], CancellationToken.None);

        turn.BudgetExceeded.Should().BeTrue();
        turn.ToolTraces.Should().BeEmpty();
        provider.Requests.Should().BeEmpty("a blocked budget gate never calls the provider");
        ledger.Records.Should().BeEmpty("a blocked turn is never metered");
    }

    [Fact]
    public async Task Ask_MissingApiKey_ReturnsFriendlyTurn()
    {
        var provider = new ScriptedProvider { ThrowMissingKey = true };
        var service = Service(provider, []);

        var turn = await service.AskAsync("pytanie", [], CancellationToken.None);

        turn.MissingApiKey.Should().BeTrue();
        turn.BudgetExceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Ask_UnknownTool_ReportsErrorBackToModel()
    {
        var provider = new ScriptedProvider();
        provider.Enqueue(AiToolTurn(("DoesNotExist", "{}")));
        provider.Enqueue(FinalTurn("Niestety nie mam takiego narzędzia."));

        var service = Service(provider, []);

        var turn = await service.AskAsync("pytanie", [], CancellationToken.None);

        turn.Answer.Should().Be("Niestety nie mam takiego narzędzia.");
        var toolMessage = provider.Requests[1].Messages.Single(m => m.Role == AiChatRole.Tool);
        toolMessage.ToolResults.Single().ResultJson.Should().Contain("Unknown tool");
    }

    [Fact]
    public async Task Ask_NeverTerminates_HitsIterationCapWithIncompleteMessage()
    {
        var tool = new FakeTool("GetTotalSpent", "{}");
        var provider = new ScriptedProvider();
        for (var i = 0; i < 10; i++)
        {
            provider.Enqueue(AiToolTurn(("GetTotalSpent", "{}")));
        }

        var service = Service(provider, [tool]);

        var turn = await service.AskAsync("pytanie", [], CancellationToken.None);

        provider.Requests.Should().HaveCount(5, "the loop is capped at five iterations");
        turn.Answer.Should().NotBeNullOrWhiteSpace();
        turn.BudgetExceeded.Should().BeFalse();
        turn.MissingApiKey.Should().BeFalse();
    }

    [Fact]
    public async Task Ask_PassesToolMenuAndChatModelToProvider()
    {
        var tool = new FakeTool("GetTotalSpent", "{}");
        var provider = new ScriptedProvider();
        provider.Enqueue(FinalTurn("Gotowe."));

        var service = Service(provider, [tool]);

        await service.AskAsync("pytanie", [], CancellationToken.None);

        var request = provider.Requests.Should().ContainSingle().Subject;
        request.Model.Should().Be(AiDefaults.ChatModel);
        request.Tools.Should().ContainSingle().Which.Name.Should().Be("GetTotalSpent");
    }

    [Fact]
    public async Task Ask_IncludesHistoryAndQuestionInFirstRequest()
    {
        var provider = new ScriptedProvider();
        provider.Enqueue(FinalTurn("Gotowe."));

        var service = Service(provider, []);
        var history = new List<ChatMessage>
        {
            new(ChatAuthor.User, "poprzednie pytanie", []),
            new(ChatAuthor.Assistant, "poprzednia odpowiedź", []),
        };

        await service.AskAsync("nowe pytanie", history, CancellationToken.None);

        var messages = provider.Requests.Single().Messages;
        messages.Should().HaveCount(3);
        messages[0].Role.Should().Be(AiChatRole.User);
        messages[0].Text.Should().Be("poprzednie pytanie");
        messages[1].Role.Should().Be(AiChatRole.Assistant);
        messages[2].Text.Should().Be("nowe pytanie");
    }

    private static ChatService Service(
        ScriptedProvider provider,
        IEnumerable<IChatTool> tools,
        IAiBudgetGate? gate = null,
        IAiUsageLedger? ledger = null,
        IPromptAnonymizer? anonymizer = null) =>
        new(
            provider,
            tools,
            gate ?? new StubBudgetGate(canProceed: true),
            ledger ?? new RecordingLedger(),
            new StubPricing(),
            anonymizer ?? new PassthroughAnonymizer(),
            NullLogger<ChatService>.Instance);

    private static AiToolTurn AiToolTurn(params (string Tool, string Args)[] calls)
    {
        var toolCalls = calls
            .Select((c, i) => new AiToolCall($"call-{i}", c.Tool, c.Args))
            .ToList();
        return new AiToolTurn(null, toolCalls);
    }

    private static AiToolTurn FinalTurn(string text) => new(text, []);

    private sealed class ScriptedProvider : IAiProvider
    {
        private readonly Queue<AiToolTurn> _turns = new();

        public string ProviderName => "Scripted";
        public List<AiToolRequest> Requests { get; } = [];
        public bool ThrowMissingKey { get; init; }

        public void Enqueue(AiToolTurn turn) => _turns.Enqueue(turn);

        public Task<AiResult<AiToolTurn>> CompleteWithToolsAsync(AiToolRequest request, CancellationToken ct)
        {
            if (ThrowMissingKey)
            {
                throw new MissingAiKeyException("no key");
            }

            Requests.Add(request);
            var turn = _turns.Count > 0 ? _turns.Dequeue() : new AiToolTurn("fallback", []);
            var usage = new AiUsage(ProviderName, request.Model, 100, 20);
            return Task.FromResult(new AiResult<AiToolTurn>(turn, usage));
        }

        public Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AiResult<string>> CompleteAsync(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTool : IChatTool
    {
        private readonly string _result;

        public FakeTool(string name, string result)
        {
            Name = name;
            _result = result;
        }

        public string Name { get; }
        public string Description => $"Fake tool {Name}.";
        public string ParametersJsonSchema => """{"type":"object","properties":{}}""";
        public List<string> ReceivedArgs { get; } = [];

        public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct)
        {
            ReceivedArgs.Add(argumentsJson);
            return Task.FromResult(_result);
        }
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
