using Coffer.Core.Ai;
using Coffer.Core.Domain;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Categorization;
using Coffer.Infrastructure.Tests.AI;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Categorization;

public class HybridCategorizerTests : CategorizationDbTest
{
    [Fact]
    public async Task Categorize_CacheHit_SkipsAi()
    {
        var groceries = await SeedCategoryAsync("Spożywcze");
        await SeedCacheAsync("lidl warszawa", groceries, CacheSource.Manual);
        var ai = new FakeAiProvider();

        var result = await Categorizer(ai).CategorizeAsync(["lidl warszawa"], CancellationToken.None);

        result["lidl warszawa"].Should().Be(groceries);
        ai.CallCount.Should().Be(0, "a cache hit never reaches the AI provider");
    }

    [Fact]
    public async Task Categorize_RuleHit_SkipsAi()
    {
        var fuel = await SeedCategoryAsync("Paliwo");
        await SeedRuleAsync("ORLEN", priority: 10, fuel);
        var ai = new FakeAiProvider();

        var result = await Categorizer(ai).CategorizeAsync(["orlen stacja"], CancellationToken.None);

        result["orlen stacja"].Should().Be(fuel);
        ai.CallCount.Should().Be(0, "a rule hit never reaches the AI provider");
    }

    [Fact]
    public async Task Categorize_Unknown_AskedAi_CachesWithAiSource()
    {
        var groceries = await SeedCategoryAsync("Spożywcze");
        await SeedCategoryAsync("Inne");
        // Categories are ordered by name: [Inne(0), Spożywcze(1)] — map the unknown to Spożywcze.
        var ai = new FakeAiProvider();
        ai.Enqueue([1]);
        var ledger = new StubLedger();

        var result = await Categorizer(ai, ledger).CategorizeAsync(["nowy sklep xyz"], CancellationToken.None);

        result["nowy sklep xyz"].Should().Be(groceries);
        ai.CallCount.Should().Be(1);
        ledger.Records.Should().ContainSingle().Which.Purpose.Should().Be(AiPurpose.Categorization);

        await using var db = Factory.CreateDbContext();
        var entry = await db.CategoryCache.SingleAsync(c => c.NormalizedDescription == "nowy sklep xyz");
        entry.CategoryId.Should().Be(groceries);
        entry.Source.Should().Be(CacheSource.Ai);
    }

    [Fact]
    public async Task Categorize_AnonymisesDescriptionBeforeSending()
    {
        await SeedCategoryAsync("Inne");
        var ai = new FakeAiProvider();
        ai.Enqueue([0]);

        await Categorizer(ai).CategorizeAsync(["przelew 61109010140000071219812874"], CancellationToken.None);

        ai.Requests.Should().ContainSingle();
        ai.Requests[0].Prompt.Should().Contain("[ACCOUNT]")
            .And.NotContain("61109010140000071219812874", "account numbers are redacted before leaving the process (hard rule #7)");
    }

    [Fact]
    public async Task Categorize_LabelledDescription_PromptIsNarrowedButCacheKeyIsTheFullDescription()
    {
        var inne = await SeedCategoryAsync("Inne");
        var ai = new FakeAiProvider();
        ai.Enqueue([0]);
        const string key = "NAZWA ODBIORCY: JAN TESTOWY ADRES ODBIORCY: UL. FIKCYJNA 1 TESTOWO TYTUŁ: CZYNSZ LUTY";

        var result = await Categorizer(ai).CategorizeAsync([key], CancellationToken.None);

        ai.Requests[0].Prompt.Should().Contain("1. \"TYTUŁ: CZYNSZ LUTY\"")
            .And.NotContain("JAN TESTOWY").And.NotContain("FIKCYJNA");
        result[key].Should().Be(inne);
        await using var db = Factory.CreateDbContext();
        (await db.CategoryCache.SingleAsync()).NormalizedDescription.Should().Be(
            key, "the cache stays keyed by the full description, so earlier cache entries keep hitting");
    }

    [Fact]
    public async Task Categorize_BatchesUnknownsInChunks()
    {
        await SeedCategoryAsync("Inne");
        var unknowns = Enumerable.Range(0, 35).Select(i => $"sklep nr {i}").ToList();
        var ai = new FakeAiProvider();
        ai.Enqueue(Enumerable.Repeat(0, 30).ToArray()); // first batch: 30 items
        ai.Enqueue(Enumerable.Repeat(0, 5).ToArray());  // second batch: 5 items

        var result = await Categorizer(ai).CategorizeAsync(unknowns, CancellationToken.None);

        ai.CallCount.Should().Be(2, "35 unknowns split into batches of 30 + 5");
        result.Values.Should().OnlyContain(v => v != null, "every unknown got a category from the AI");
    }

    [Fact]
    public async Task Categorize_MalformedThenValid_RetriesOnce()
    {
        var inne = await SeedCategoryAsync("Inne");
        var ai = new FakeAiProvider();
        ai.Enqueue([5]);  // out of range → invalid
        ai.Enqueue([0]);  // valid on retry

        var result = await Categorizer(ai).CategorizeAsync(["dziwny opis"], CancellationToken.None);

        result["dziwny opis"].Should().Be(inne);
        ai.CallCount.Should().Be(2, "an invalid index array triggers exactly one retry");
    }

    [Fact]
    public async Task Categorize_MalformedTwice_LeavesNullAndUncached()
    {
        await SeedCategoryAsync("Inne");
        var ai = new FakeAiProvider();
        ai.Enqueue([9]); // invalid
        ai.Enqueue([9]); // invalid again

        var result = await Categorizer(ai).CategorizeAsync(["beznadziejny opis"], CancellationToken.None);

        result["beznadziejny opis"].Should().BeNull("after one retry the categoriser gives up rather than mislabel");
        ai.CallCount.Should().Be(2);

        await using var db = Factory.CreateDbContext();
        (await db.CategoryCache.AnyAsync()).Should().BeFalse("a failed AI categorisation writes no cache entry");
    }

    [Fact]
    public async Task Categorize_ProviderThrows_DoesNotBreakImport()
    {
        await SeedCategoryAsync("Inne");
        var ai = new FakeAiProvider { Throw = true };

        var result = await Categorizer(ai).CategorizeAsync(["cokolwiek"], CancellationToken.None);

        result["cokolwiek"].Should().BeNull("a provider failure leaves the row uncategorised, never throws into the import");
        ai.CallCount.Should().Be(2, "the throw is retried once before giving up");
    }

    [Fact]
    public async Task Categorize_BudgetDenied_SkipsAiEntirely()
    {
        await SeedCategoryAsync("Inne");
        var ai = new FakeAiProvider();

        var result = await Categorizer(ai, gate: new StubBudgetGate(canProceed: false))
            .CategorizeAsync(["cokolwiek"], CancellationToken.None);

        result["cokolwiek"].Should().BeNull();
        ai.CallCount.Should().Be(0, "a blocked budget gate never calls the provider");
    }

    [Fact]
    public async Task Categorize_SendsAndPricesEveryBatchOnTheCategorizationRoute()
    {
        await SeedCategoryAsync("Inne");
        var unknowns = Enumerable.Range(0, 35).Select(i => $"sklep nr {i}").ToList();
        var ai = new FakeAiProvider();
        ai.Enqueue(Enumerable.Repeat(0, 30).ToArray());
        ai.Enqueue(Enumerable.Repeat(0, 5).ToArray());
        var routes = new FixedRouteResolver(categorization: TestRoutes.Routed);
        var pricing = new RecordingPricing();

        await Categorizer(ai, routes: routes, pricing: pricing).CategorizeAsync(unknowns, CancellationToken.None);

        routes.ResolvedRoles.Should().Equal([AiRole.Categorization], "the route is resolved once per run");
        ai.Requests.Should().HaveCount(2).And.OnlyContain(
            r => r.Provider == TestRoutes.Routed.Provider && r.Model == TestRoutes.Routed.Model);
        pricing.PreCallEstimates.Should().HaveCount(2).And.OnlyContain(
            e => e.Provider == TestRoutes.Routed.Provider && e.Model == TestRoutes.Routed.Model);
    }

    [Fact]
    public async Task Categorize_UnknownProvider_GivesUpWithoutRetrying()
    {
        await SeedCategoryAsync("Inne");
        var ai = new FakeAiProvider { ThrowUnknownProvider = true };

        var result = await Categorizer(ai).CategorizeAsync(["cokolwiek"], CancellationToken.None);

        result["cokolwiek"].Should().BeNull();
        ai.CallCount.Should().Be(1, "an unconfigured provider is not a transient failure, so it is not retried");
    }

    private HybridCategorizer Categorizer(
        FakeAiProvider provider,
        IAiUsageLedger? ledger = null,
        IAiBudgetGate? gate = null,
        IAiRouteResolver? routes = null,
        IAiPricing? pricing = null) =>
        new(
            Factory,
            new RuleEngine(NullLogger<RuleEngine>.Instance),
            provider,
            gate ?? new StubBudgetGate(canProceed: true),
            ledger ?? new StubLedger(),
            pricing ?? new AiPricing(),
            new FixedPromptAnonymizer(),
            routes ?? new FixedRouteResolver(),
            NullLogger<HybridCategorizer>.Instance);

    private async Task<Guid> SeedCategoryAsync(string name)
    {
        await using var db = await MigratedContextAsync();
        var category = new Category { Id = Guid.NewGuid(), Name = name, Color = "#34C759" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task SeedRuleAsync(string pattern, int priority, Guid categoryId)
    {
        await using var db = await MigratedContextAsync();
        db.Rules.Add(new Rule
        {
            Id = Guid.NewGuid(),
            Pattern = pattern,
            Priority = priority,
            CategoryId = categoryId,
            IsEnabled = true,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedCacheAsync(string normalized, Guid categoryId, CacheSource source)
    {
        await using var db = await MigratedContextAsync();
        db.CategoryCache.Add(new CategoryCache
        {
            Id = Guid.NewGuid(),
            NormalizedDescription = normalized,
            CategoryId = categoryId,
            Source = source,
            HitCount = 1,
            LastUsedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private sealed class FakeAiProvider : IAiProvider
    {
        private readonly Queue<int[]> _responses = new();

        public string ProviderName => "FakeAI";
        public int CallCount { get; private set; }
        public List<AiRequest> Requests { get; } = [];
        public bool Throw { get; init; }
        public bool ThrowUnknownProvider { get; init; }

        public void Enqueue(int[] indexes) => _responses.Enqueue(indexes);

        public Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct)
        {
            CallCount++;
            Requests.Add(request);
            if (Throw)
            {
                throw new InvalidOperationException("simulated provider failure");
            }

            if (ThrowUnknownProvider)
            {
                throw new UnknownAiProviderException(request.Provider);
            }

            var indexes = _responses.Count > 0 ? _responses.Dequeue() : [];
            var usage = new AiUsage(ProviderName, request.Model, 100, 20);
            var result = new AiResult<TResult>((TResult)(object)indexes, usage);
            return Task.FromResult(result);
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

    private sealed class StubLedger : IAiUsageLedger
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
}
