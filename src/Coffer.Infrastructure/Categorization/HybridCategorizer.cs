using System.Globalization;
using System.Text;
using Coffer.Core.Ai;
using Coffer.Core.Categorization;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Categorization;

/// <summary>
/// Hybrid categoriser (Phase 10-C): <b>cache → rules → AI batch</b>. Cache and rule hits are
/// resolved exactly as the deterministic <see cref="RuleCacheCategorizer"/> does (and written
/// back so the next encounter is free). Whatever is left unknown is anonymised (hard rule #7),
/// sent to the AI provider in batches behind the <see cref="IAiBudgetGate"/> at
/// <see cref="AiPriority.Critical"/>, validated, ledgered, and cached with
/// <see cref="CacheSource.Ai"/>.
/// <para>
/// The AI stage never breaks an import: a denied budget, a missing key, a network error, or
/// malformed output (after one retry) leaves the affected descriptions uncategorised
/// (<c>null</c>) and logs — the owner can recategorise later. The DB connection is not held
/// open across the network calls: cache/rule resolution and the AI write-back are two separate
/// short-lived contexts.
/// </para>
/// </summary>
public sealed class HybridCategorizer : ICategorizer
{
    private const int _batchSize = 30;
    private const int _charsPerToken = 4;
    private const int _outputTokensPerItem = 4;

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;
    private readonly ICategoryRuleEngine _ruleEngine;
    private readonly IAiProvider _provider;
    private readonly IAiBudgetGate _budgetGate;
    private readonly IAiUsageLedger _ledger;
    private readonly IAiPricing _pricing;
    private readonly IPromptAnonymizer _anonymizer;
    private readonly IAiSettings _settings;
    private readonly ILogger<HybridCategorizer> _logger;

    public HybridCategorizer(
        IDbContextFactory<CofferDbContext> contextFactory,
        ICategoryRuleEngine ruleEngine,
        IAiProvider provider,
        IAiBudgetGate budgetGate,
        IAiUsageLedger ledger,
        IAiPricing pricing,
        IPromptAnonymizer anonymizer,
        IAiSettings settings,
        ILogger<HybridCategorizer> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(ruleEngine);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(budgetGate);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(pricing);
        ArgumentNullException.ThrowIfNull(anonymizer);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _ruleEngine = ruleEngine;
        _provider = provider;
        _budgetGate = budgetGate;
        _ledger = ledger;
        _pricing = pricing;
        _anonymizer = anonymizer;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, Guid?>> CategorizeAsync(
        IReadOnlyCollection<string> normalizedDescriptions, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(normalizedDescriptions);

        var keys = normalizedDescriptions
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var result = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        if (keys.Count == 0)
        {
            return result;
        }

        // Phase A — cache → rules in one short-lived context (no network held open).
        var categories = await ResolveDeterministicAsync(keys, result, ct).ConfigureAwait(false);

        // Phase B — AI batch over the descriptions still unknown.
        var unknowns = keys.Where(k => result[k] is null).ToList();
        if (unknowns.Count == 0 || categories.Count == 0)
        {
            return result;
        }

        var aiResolved = await CategorizeWithAiAsync(unknowns, categories, ct).ConfigureAwait(false);
        if (aiResolved.Count == 0)
        {
            return result;
        }

        foreach (var (key, categoryId) in aiResolved)
        {
            result[key] = categoryId;
        }

        // Phase C — persist AI results to the cache (these keys were unknown, so plain inserts).
        await PersistAiCacheAsync(aiResolved, ct).ConfigureAwait(false);
        return result;
    }

    private async Task<IReadOnlyList<(Guid Id, string Name)>> ResolveDeterministicAsync(
        IReadOnlyList<string> keys, Dictionary<string, Guid?> result, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var cached = await db.CategoryCache
            .Where(c => keys.Contains(c.NormalizedDescription))
            .ToDictionaryAsync(c => c.NormalizedDescription, c => c, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

        var rules = await db.Rules.AsNoTracking()
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var categories = await db.Categories.AsNoTracking()
            .Where(c => !c.IsArchived)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var now = DateTime.UtcNow;
        foreach (var key in keys)
        {
            if (cached.TryGetValue(key, out var hit))
            {
                hit.HitCount++;
                hit.LastUsedAt = now;
                result[key] = hit.CategoryId;
                continue;
            }

            var ruleMatch = _ruleEngine.Match(key, rules);
            result[key] = ruleMatch;
            if (ruleMatch is { } categoryId)
            {
                db.CategoryCache.Add(new CategoryCache
                {
                    Id = Guid.NewGuid(),
                    NormalizedDescription = key,
                    CategoryId = categoryId,
                    Source = CacheSource.Rule,
                    HitCount = 1,
                    LastUsedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return categories.Select(c => (c.Id, c.Name)).ToList();
    }

    private async Task<Dictionary<string, Guid?>> CategorizeWithAiAsync(
        IReadOnlyList<string> unknowns,
        IReadOnlyList<(Guid Id, string Name)> categories,
        CancellationToken ct)
    {
        var resolved = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        var model = await _settings.GetCategorizationModelAsync(ct).ConfigureAwait(false);
        var categoryList = string.Join(", ", categories.Select(c => c.Name));

        for (var offset = 0; offset < unknowns.Count; offset += _batchSize)
        {
            var batch = unknowns.Skip(offset).Take(_batchSize).ToList();
            var indexes = await CategorizeBatchAsync(batch, categories, categoryList, model, ct)
                .ConfigureAwait(false);
            if (indexes is null)
            {
                // Budget denied or the call failed after one retry — stop AI for this import;
                // the remaining unknowns stay uncategorised and can be recategorised later.
                break;
            }

            for (var i = 0; i < batch.Count; i++)
            {
                resolved[batch[i]] = categories[indexes[i]].Id;
            }
        }

        return resolved;
    }

    /// <summary>
    /// Categorises a single batch. Returns a validated, batch-length array of 0-based category
    /// indexes, or <c>null</c> when the budget gate blocks the call or the provider fails / returns
    /// unusable output after one retry. Never throws into the import.
    /// </summary>
    private async Task<int[]?> CategorizeBatchAsync(
        IReadOnlyList<string> batch,
        IReadOnlyList<(Guid Id, string Name)> categories,
        string categoryList,
        string model,
        CancellationToken ct)
    {
        var prompt = BuildPrompt(batch, categoryList);

        var estInputTokens = (_systemPrompt.Length + prompt.Length) / _charsPerToken;
        var estOutputTokens = batch.Count * _outputTokensPerItem;
        var estimate = _pricing.Estimate(model, estInputTokens, estOutputTokens);
        if (!await _budgetGate.CanProceedAsync(estimate.Pln, AiPriority.Critical, ct).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "Budget gate blocked AI categorisation of {Count} description(s); leaving them uncategorised.",
                batch.Count);
            return null;
        }

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var request = new AiRequest
            {
                Prompt = attempt == 1 ? prompt : prompt + RetryAddendum(batch.Count, categories.Count),
                Model = model,
                SystemPrompt = _systemPrompt,
                MaxTokens = 512,
                Temperature = 0.0,
            };

            try
            {
                var response = await _provider.CompleteJsonAsync<int[]>(request, ct).ConfigureAwait(false);
                await _ledger.RecordAsync(response.Usage, AiPurpose.Categorization, ct).ConfigureAwait(false);

                if (IsValid(response.Value, batch.Count, categories.Count))
                {
                    return response.Value;
                }

                _logger.LogWarning(
                    "AI categorisation returned an invalid index array for a batch of {Count} (attempt {Attempt}/2).",
                    batch.Count, attempt);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "AI categorisation call failed for a batch of {Count} (attempt {Attempt}/2).",
                    batch.Count, attempt);
            }
        }

        _logger.LogError(
            "AI categorisation gave up on a batch of {Count} after one retry; leaving it uncategorised.",
            batch.Count);
        return null;
    }

    private async Task PersistAiCacheAsync(Dictionary<string, Guid?> aiResolved, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        foreach (var (key, categoryId) in aiResolved)
        {
            if (categoryId is not { } id)
            {
                continue;
            }

            db.CategoryCache.Add(new CategoryCache
            {
                Id = Guid.NewGuid(),
                NormalizedDescription = key,
                CategoryId = id,
                Source = CacheSource.Ai,
                HitCount = 1,
                LastUsedAt = now,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private string BuildPrompt(IReadOnlyList<string> batch, string categoryList)
    {
        var sb = new StringBuilder();
        sb.Append("Categories: [").Append(categoryList).Append("]\n\n");
        sb.Append("Transactions:\n");
        for (var i = 0; i < batch.Count; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{i + 1}. \"{_anonymizer.Anonymize(batch[i])}\"\n");
        }

        sb.Append("\nReturn ONLY a JSON array of ").Append(batch.Count)
            .Append(" integers (0-based indexes into the categories array), in the same order as the transactions, e.g.: [0, 4, 3].");
        return sb.ToString();
    }

    private static string RetryAddendum(int expectedCount, int categoryCount) =>
        $"\n\nYour previous response was not a valid JSON array of {expectedCount} integers, each in [0, {categoryCount}). Return ONLY the JSON array.";

    private static bool IsValid(int[]? indexes, int expectedCount, int categoryCount) =>
        indexes is not null
        && indexes.Length == expectedCount
        && indexes.All(i => i >= 0 && i < categoryCount);

    private const string _systemPrompt =
        "You are a Polish personal finance categorizer. Assign each transaction to exactly one "
        + "category from the supplied list. Respond with ONLY a JSON array of 0-based integer "
        + "indexes into the categories array, in the same order as the transactions. No prose.";
}
