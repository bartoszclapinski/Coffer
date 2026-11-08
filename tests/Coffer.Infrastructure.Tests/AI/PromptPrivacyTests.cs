using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coffer.Core.Ai;
using Coffer.Core.Anomalies;
using Coffer.Core.Chat;
using Coffer.Core.Domain;
using Coffer.Core.Goals;
using Coffer.Core.Parsing;
using Coffer.Core.Planning;
using Coffer.Core.Security;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Categorization;
using Coffer.Infrastructure.Chat;
using Coffer.Infrastructure.Parsing.Ai;
using Coffer.Infrastructure.Parsing.Pko;
using Coffer.Infrastructure.Parsing.Polish;
using Coffer.Infrastructure.Planning;
using Coffer.Infrastructure.Tests.Categorization;
using Coffer.Shared.Parsing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.AI;

/// <summary>
/// Sprint 30 definition of done for AI privacy: synthetic PKO "Historia rachunku" rows carrying a
/// person's name, street address, phone number, PESEL, account numbers and the owner's own name go
/// through every AI consumer (categoriser, chat, anomaly commentator, advisor report, cash-flow
/// explainer, AI fallback parser) with the production anonymiser, and no prompt contains any of
/// them, while the merchant/title signal (<c>BIEDRONKA 1234</c>, <c>Zwrot za bilety</c>) survives.
/// Every value below is invented.
/// </summary>
public class PromptPrivacyTests : CategorizationDbTest
{
    private const string OwnerNames = "Łukasz Źdźbło";

    private const string Header =
        "\"Data operacji\",\"Data waluty\",\"Typ transakcji\",\"Kwota\",\"Waluta\",\"Saldo po transakcji\","
        + "\"Opis transakcji\",\"\",\"\",\"\",\"\"";

    private static readonly string[] _rows =
    [
        // Outgoing transfer to a person: account, name, street address, title.
        "\"2026-02-03\",\"2026-02-03\",\"Przelew z rachunku\",\"-1200.00\",\"PLN\",\"+3800.00\","
            + "\"Rachunek odbiorcy: 11 1111 1111 1111 1111 1111 1111\",\"Nazwa odbiorcy: JAN TESTOWY\","
            + "\"Adres odbiorcy: UL. FIKCYJNA 12/3 00-001 TESTOWO\",\"Tytuł: Zwrot za bilety\",\"\"",

        // BLIK to a phone number: phones in the title and in the labelled field.
        "\"2026-02-04\",\"2026-02-04\",\"Przelew na telefon\",\"-50.00\",\"PLN\",\"+3750.00\","
            + "\"Rachunek odbiorcy: 22 2222 2222 2222 2222 2222 2222\",\"Nazwa odbiorcy: ANNA PRZYKLADOWA\","
            + "\"Tytuł: Pizza OD: 48500600700 DO: 48501234567\",\"Numer telefonu: +48 501 234 567\",\"\"",

        // Tax transfer: PESEL in the title and in the identifier field.
        "\"2026-02-05\",\"2026-02-05\",\"Przelew podatkowy\",\"-300.00\",\"PLN\",\"+3450.00\","
            + "\"Rachunek odbiorcy: 33 3333 3333 3333 3333 3333 3333\",\"Nazwa odbiorcy: URZAD SKARBOWY W TESTOWIE\","
            + "\"Tytuł: /TI/P44051401458/OKR/26M01/SFP/PIT37/\",\"Nazwa i nr identyfikatora: P 44051401458\",\"\"",

        // Card payment: the merchant lives in the location; masked card number.
        "\"2026-02-06\",\"2026-02-05\",\"Płatność kartą\",\"-49.99\",\"PLN\",\"+3400.01\","
            + "\"Tytuł: 00000079887766554433221\",\"Lokalizacja: Adres: BIEDRONKA 1234 Miasto: TORUN Kraj: POLSKA\","
            + "\"Data wykonania operacji: 2026-02-05\",\"Oryginalna kwota operacji: 49.99\",\"Numer karty: 425125******1234\"",

        // Transfer from the owner's other account: owner name (ASCII export and diacritics), owner address.
        "\"2026-02-07\",\"2026-02-07\",\"Przelew na rachunek\",\"+500.00\",\"PLN\",\"+3900.01\","
            + "\"Nazwa nadawcy: LUKASZ ZDZBLO\",\"Adres nadawcy: UL. PRZYKLADOWA 7 TORUN\","
            + "\"Tytuł: Przelew własny Łukasz Źdźbło\",\"\",\"\"",

        // Rent to a sole trader: a quoted business name holding the person's name, an address with
        // a semicolon, a street address in the title and an earlier payee named in reversed order.
        "\"2026-02-08\",\"2026-02-08\",\"Przelew z rachunku\",\"-250.00\",\"PLN\",\"+3650.01\","
            + "\"Nazwa odbiorcy: P.H.U. \"\"ANEX\"\" EWA FIKCYJNA\",\"Adres odbiorcy: UL. POLNA 1; 87-100 TESTOWO\","
            + "\"Tytuł: CZYNSZ UL. KWIATOWA 5/3 87-100 TORUN dla PRZYKLADOWA ANNA\",\"\",\"\"",
    ];

    /// <summary>Personal data that must never reach a provider (compared case-insensitively).</summary>
    private static readonly string[] _forbidden =
    [
        "JAN TESTOWY", "ANNA PRZYKLADOWA", "PRZYKLADOWA", "EWA FIKCYJNA", "ANEX",
        "FIKCYJNA", "00-001", "TESTOWO", "POLNA", "KWIATOWA", "87-100",
        "500600700", "501234567", "501 234 567",
        "44051401458",
        "1111 1111", "2222 2222", "3333 3333", "425125",
        "LUKASZ", "ZDZBLO", "Łukasz", "Źdźbło",
    ];

    [Fact]
    public async Task Categorizer_PromptHasNoPersonalDataButKeepsMerchantSignal()
    {
        await SeedCategoriesAsync();
        var parsed = await ParsePkoAsync();
        var provider = new RecordingProvider(request => Indexes(request.Prompt));
        var categorizer = new HybridCategorizer(
            Factory,
            new RuleEngine(NullLogger<RuleEngine>.Instance),
            provider,
            new AllowingBudgetGate(),
            new NullLedger(),
            new AiPricing(),
            ProductionAnonymizer(),
            new FixedRouteResolver(),
            NullLogger<HybridCategorizer>.Instance);

        var keys = parsed.Select(t => DescriptionNormalizer.Normalize(t.Description)).ToList();
        await categorizer.CategorizeAsync(keys, CancellationToken.None);

        var prompt = provider.Prompts.Should().ContainSingle().Subject;
        AssertNoPersonalData(prompt);
        prompt.Should().ContainAll("BIEDRONKA 1234", "TORUN", "ZWROT ZA BILETY", "PIZZA", "PIT37", "CZYNSZ [ADDRESS] TORUN");
        prompt.Should().Contain(
            "NAZWA ODBIORCY: URZAD SKARBOWY W TESTOWIE", "a legal-entity counterparty is the category signal and is kept");
        prompt.Should().NotContainAny("ADRES ODBIORCY", "NUMER TELEFONU", "IDENTYFIKATORA", "NUMER KARTY")
            .And.NotContain("NAZWA ODBIORCY: [PARTY_", "a person counterparty's name field is not sent to the categoriser")
            .And.Contain("DLA [PARTY_", "a payee named (in reversed order) in a kept title is pseudonymised");
    }

    [Fact]
    public async Task Chat_ToolResultsQuestionAndHistoryHaveNoPersonalData_AnswerIsRestored()
    {
        await SeedTransactionsAsync(await ParsePkoAsync());
        var provider = new ScriptedChatProvider();
        provider.Enqueue(_ => new AiToolTurn(null,
        [
            new AiToolCall("c1", "GetTransactions", """{"from":"2026-02-01","to":"2026-02-28"}"""),
            new AiToolCall("c2", "GetCashFlowProjection", "{}"),
        ]));
        provider.Enqueue(request => new AiToolTurn("Odbiorcy: " + string.Join(", ", Tokens(Serialize(request))), []));

        var flows = new FakeTool("GetCashFlowProjection", """{"events":[{"name":"JAN TESTOWY","amount":-1200.00}]}""");
        var service = new ChatService(
            provider,
            new FixedRouteResolver(),
            [new GetTransactionsTool(Factory), flows],
            new AllowingBudgetGate(),
            new NullLedger(),
            new AiPricing(),
            ProductionAnonymizer(),
            NullLogger<ChatService>.Instance);

        var turn = await service.AskAsync("Komu przelałem pieniądze? Mój PESEL to 44051401458.", [], CancellationToken.None);

        provider.Requests.Should().HaveCount(2);
        foreach (var request in provider.Requests)
        {
            AssertNoPersonalData(Serialize(request));
        }

        Serialize(provider.Requests[1]).Should().Contain("BIEDRONKA 1234");
        turn.Answer.Should().Contain("JAN TESTOWY").And.Contain("ANNA PRZYKLADOWA").And.NotContain("[PARTY_");

        // The restored answer goes back as history on the next question: it is redacted again.
        provider.Enqueue(_ => new AiToolTurn("ok", []));
        var history = new[]
        {
            new ChatMessage(ChatAuthor.User, "Komu przelałem pieniądze?", []),
            new ChatMessage(ChatAuthor.Assistant, turn.Answer, []),
        };
        await service.AskAsync("A ile wysłałem Łukasz Źdźbło?", history, CancellationToken.None);

        AssertNoPersonalData(Serialize(provider.Requests[2]));
    }

    [Fact]
    public async Task AnomalyCommentator_PromptHasNoPersonalData_CommentIsRestored()
    {
        var parsed = await ParsePkoAsync();
        await SeedTransactionsAsync(parsed);
        var merchant = parsed[0].Merchant!;
        var candidate = new AnomalyCandidate(
            AnomalyType.NewMerchant,
            1200d,
            $"new-merchant:{merchant}",
            $"Nowy sprzedawca: {merchant}",
            $"Pierwsza płatność u sprzedawcy „{merchant}” (1 200,00 zł) w analizowanym okresie.",
            Guid.NewGuid(),
            1200m,
            new DateOnly(2026, 2, 3),
            new DateOnly(2026, 2, 3),
            new Dictionary<string, string> { ["merchant"] = merchant, ["amount"] = "1200" });
        var provider = new RecordingProvider(request =>
            $$"""[{"index":0,"title":"Nowy odbiorca {{Tokens(request.Prompt).Single()}}","description":"Pierwszy przelew."}]""");
        var commentator = new AnomalyCommentator(
            provider, new FixedRouteResolver(), new AllowingBudgetGate(), new NullLedger(), new AiPricing(), ProductionAnonymizer(),
            NullLogger<AnomalyCommentator>.Instance);

        var result = await commentator.CommentAsync([candidate], CancellationToken.None);

        AssertNoPersonalData(provider.Prompts.Should().ContainSingle().Subject);
        result[0].Title.Should().Be("Nowy odbiorca JAN TESTOWY");
    }

    [Fact]
    public async Task CashFlowExplainer_PromptHasNoPersonalData_NarrativeIsRestored()
    {
        var parsed = await ParsePkoAsync();
        await SeedTransactionsAsync(parsed);
        var projection = new CashFlowProjectionEngine().Project(
            [Flow(parsed[0].Merchant!, FlowDirection.Outflow, 1200m), Flow("Wynagrodzenie", FlowDirection.Inflow, 6000m)],
            3000m,
            new DateOnly(2026, 3, 1),
            60);
        var provider = new RecordingProvider(request => $"Najpierw wychodzi przelew do {Tokens(request.Prompt).Single()}.");
        var explainer = new CashFlowExplainer(
            provider, new FixedRouteResolver(), new AllowingBudgetGate(), new NullLedger(), new AiPricing(), ProductionAnonymizer(),
            NullLogger<CashFlowExplainer>.Instance);

        var explanation = await explainer.ExplainAsync(projection, CancellationToken.None);

        var prompt = provider.Prompts.Should().ContainSingle().Subject;
        AssertNoPersonalData(prompt);
        prompt.Should().Contain("Wynagrodzenie", "an owner-typed flow name is not a counterparty");
        explanation.Narrative.Should().Be("Najpierw wychodzi przelew do JAN TESTOWY.");
    }

    [Fact]
    public async Task AdvisorReport_PromptHasNoOwnerNameOrCounterparty()
    {
        await SeedTransactionsAsync(await ParsePkoAsync());
        var goalId = Guid.NewGuid();
        var result = new GoalFeasibilityResult
        {
            GoalId = goalId,
            Status = GoalStatus.AtRisk,
            EffectiveTarget = 8000m,
            ProjectedDate = new DateOnly(2027, 1, 1),
            RequiredMonthlySaving = 1000m,
            CurrentMonthlySaving = 400m,
            ConfidenceScore = 0.6m,
            AlternativeScenarios = [],
            Risks = [new RiskFactor("tight-timeline", "Łukasz Źdźbło: napięty termin, kontakt +48 501 234 567.")],
            DiagnosticSummary = "diag",
        };
        var context = new FinancialContext
        {
            MonthlyIncome = 6000m,
            MonthlyFixedExpenses = 2500m,
            MonthlyVariableAvg = 1200m,
            MonthlyVariableStdDev = 150m,
            OtherActiveGoals = [],
            CategoryAverages6m = new Dictionary<string, decimal>(),
            SeasonalityModifiers = new Dictionary<int, decimal>(),
            Today = new DateOnly(2026, 3, 1),
        };
        var provider = new RecordingProvider(_ => """{"perGoalRisks":{},"suggestions":[]}""");
        var generator = new AdvisorReportGenerator(
            provider, new FixedRouteResolver(), new AllowingBudgetGate(), new NullLedger(), new AiPricing(), ProductionAnonymizer(),
            NullLogger<AdvisorReportGenerator>.Instance);

        await generator.GenerateAsync(
            [result], context, [new CategorySpending("Prezenty dla JAN TESTOWY", 540m, 211m)], context.Today, CancellationToken.None);

        AssertNoPersonalData(provider.Prompts.Should().ContainSingle().Subject);
    }

    [Fact]
    public async Task AiFallbackParser_PromptHasNoPersonalData_KeepsCsvStructureAndRestoresNames()
    {
        var csv = Header + "\n" + string.Join('\n', _rows) + "\n";
        var provider = new RecordingProvider(request =>
        {
            var payee = Tokens(request.Prompt).First();
            return $$"""
                {"currency":"PLN","transactions":[
                  {"date":"2026-02-03","amount":-1200.00,"currency":"PLN","description":"Przelew do {{payee}}","merchant":"{{payee}}"}
                ]}
                """;
        });
        var settings = new StubAiSettings { OwnerNames = OwnerNames };
        var parser = new AiAssistedParser(
            provider,
            new FixedRouteResolver(),
            new AiRouteReadiness([new ClaudeProvider(new KeyedSecretStore())]),
            new AllowingBudgetGate(),
            new NullLedger(),
            new AiPricing(),
            new PromptAnonymizer(settings, new StoredCounterpartyNameSource(Factory)),
            settings,
            NullLogger<AiAssistedParser>.Instance);
        await (await MigratedContextAsync()).DisposeAsync();

        var result = await parser.ParseAsync(
            new StatementInput(new MemoryStream(Encoding.UTF8.GetBytes(csv)), StatementFormat.Csv),
            CancellationToken.None);

        var prompt = provider.Prompts.Should().ContainSingle().Subject;
        AssertNoPersonalData(prompt);
        prompt.Should().ContainAll("\"-1200.00\"", "\"2026-02-03\"", "BIEDRONKA 1234", "\"Tytuł: Zwrot za bilety\"");
        result.Warnings.Should().NotContain(AiAssistedParser.OwnerNameUnsetWarning);
        result.Transactions.Single().Merchant.Should().Be("JAN TESTOWY", "pseudonyms are restored for local storage");
    }

    private static void AssertNoPersonalData(string prompt)
    {
        foreach (var value in _forbidden)
        {
            prompt.Should().NotContainEquivalentOf(value, "personal data must never reach an AI provider (hard rule #7)");
        }
    }

    private PromptAnonymizer ProductionAnonymizer() =>
        new(new StubAiSettings { OwnerNames = OwnerNames }, new StoredCounterpartyNameSource(Factory));

    private static async Task<IReadOnlyList<ParsedTransaction>> ParsePkoAsync()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var csv = Header + "\r\n" + string.Join("\r\n", _rows) + "\r\n";
        var bytes = Encoding.GetEncoding(1250).GetBytes(csv);
        var result = await new PkoHistoriaCsvParser().ParseAsync(
            new StatementInput(new MemoryStream(bytes), StatementFormat.Csv),
            CancellationToken.None);
        result.Transactions.Should().HaveCount(_rows.Length);
        return result.Transactions;
    }

    private async Task SeedCategoriesAsync()
    {
        await using var db = await MigratedContextAsync();
        db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Inne", Color = "#34C759" });
        await db.SaveChangesAsync();
    }

    private async Task SeedTransactionsAsync(IReadOnlyList<ParsedTransaction> parsed)
    {
        await using var db = await MigratedContextAsync();
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Konto testowe",
            BankCode = "PKO_BP",
            AccountNumber = "PL00TEST",
            Currency = "PLN",
            Type = AccountType.Checking,
            CreatedAt = DateTime.UtcNow,
        };
        var session = new ImportSession
        {
            Id = Guid.NewGuid(),
            FileName = "synthetic.csv",
            FileHash = "SYNTHETIC",
            BankCode = "PKO_BP",
            PeriodFrom = new DateOnly(2026, 2, 1),
            PeriodTo = new DateOnly(2026, 2, 28),
            ImportedAt = DateTime.UtcNow,
            Status = ImportStatus.Completed,
        };
        db.Accounts.Add(account);
        db.ImportSessions.Add(session);
        foreach (var t in parsed)
        {
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                ImportSessionId = session.Id,
                Date = t.Date,
                BookingDate = t.BookingDate,
                Amount = t.Amount,
                Currency = t.Currency,
                Description = t.Description,
                NormalizedDescription = DescriptionNormalizer.Normalize(t.Description),
                Merchant = t.Merchant,
                Hash = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    private static RecurringFlow Flow(string name, FlowDirection direction, decimal amount) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Direction = direction,
        IntervalMonths = 1,
        AnchorDayOfMonth = 10,
        TypicalAmount = amount,
        Currency = "PLN",
        IsActive = true,
        Source = FlowSource.Detected,
        CreatedAt = DateTime.UtcNow,
    };

    private static string Indexes(string prompt)
    {
        var count = Regex.Matches(prompt, @"^\d+\. ", RegexOptions.Multiline).Count;
        return "[" + string.Join(',', Enumerable.Repeat(0, count)) + "]";
    }

    private static List<string> Tokens(string text) =>
        Regex.Matches(text, @"\[PARTY_\d+\]").Select(m => m.Value).Distinct().ToList();

    private static string Serialize(AiToolRequest request)
    {
        var sb = new StringBuilder(request.SystemPrompt);
        foreach (var message in request.Messages)
        {
            sb.Append('\n').Append(message.Text);
            foreach (var call in message.ToolCalls)
            {
                sb.Append('\n').Append(call.ArgumentsJson);
            }

            foreach (var result in message.ToolResults)
            {
                // Decode JSON escapes so an escaped name (Łukasz) cannot hide from the assertion.
                sb.Append('\n').Append(Regex.Unescape(result.ResultJson));
            }
        }

        return sb.ToString();
    }

    /// <summary>Answers single-shot requests through a responder and records every prompt.</summary>
    private sealed class RecordingProvider(Func<AiRequest, string> respond) : IAiProvider
    {
        private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

        public string ProviderName => "Recording";

        public List<string> Prompts { get; } = [];

        public Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct)
        {
            Prompts.Add(request.SystemPrompt + "\n" + request.Prompt);
            var value = JsonSerializer.Deserialize<TResult>(respond(request), _options)!;
            return Task.FromResult(new AiResult<TResult>(value, Usage(request.Model)));
        }

        public Task<AiResult<string>> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Prompts.Add(request.SystemPrompt + "\n" + request.Prompt);
            return Task.FromResult(new AiResult<string>(respond(request), Usage(request.Model)));
        }

        public Task<AiResult<AiToolTurn>> CompleteWithToolsAsync(AiToolRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        private AiUsage Usage(string model) => new(ProviderName, model, 100, 20);
    }

    private sealed class ScriptedChatProvider : IAiProvider
    {
        private readonly Queue<Func<AiToolRequest, AiToolTurn>> _turns = new();

        public string ProviderName => "Scripted";

        public List<AiToolRequest> Requests { get; } = [];

        public void Enqueue(Func<AiToolRequest, AiToolTurn> turn) => _turns.Enqueue(turn);

        public Task<AiResult<AiToolTurn>> CompleteWithToolsAsync(AiToolRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var turn = _turns.Dequeue()(request);
            return Task.FromResult(new AiResult<AiToolTurn>(turn, new AiUsage(ProviderName, request.Model, 100, 20)));
        }

        public Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AiResult<string>> CompleteAsync(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTool(string name, string result) : IChatTool
    {
        public string Name => name;

        public string Description => $"Fake tool {name}.";

        public string ParametersJsonSchema => """{"type":"object","properties":{}}""";

        public Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct) => Task.FromResult(result);
    }

    private sealed class AllowingBudgetGate : IAiBudgetGate
    {
        public Task<bool> CanProceedAsync(decimal estimatedCostPln, AiPriority priority, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class NullLedger : IAiUsageLedger
    {
        public Task RecordAsync(AiUsage usage, string purpose, CancellationToken ct) => Task.CompletedTask;

        public Task<decimal> GetCurrentMonthSpendPlnAsync(CancellationToken ct) => Task.FromResult(0m);

        public Task<IReadOnlyList<AiSpendByPurpose>> GetCurrentMonthByPurposeAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AiSpendByPurpose>>([]);
    }

    private sealed class KeyedSecretStore : ISecretStore
    {
        public Task<string?> GetSecretAsync(string name, CancellationToken ct) => Task.FromResult<string?>("sk-test");

        public Task SetSecretAsync(string name, string value, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteSecretAsync(string name, CancellationToken ct) => Task.CompletedTask;
    }
}
