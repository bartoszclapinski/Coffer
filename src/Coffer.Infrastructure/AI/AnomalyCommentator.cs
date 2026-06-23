using System.Globalization;
using System.Text;
using Coffer.Core.Ai;
using Coffer.Core.Anomalies;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// 13-B commentary: rewrites the templated title/description of the top anomaly candidates into
/// natural Polish with the reasoning-tier model (doc 04, "statistics detect, AI explains"). One
/// batched provider call per run, gated by <see cref="IAiBudgetGate"/> at
/// <see cref="AiPriority.Normal"/> and metered once as <see cref="AiPurpose.AnomalyComment"/>.
/// The prompt is anonymised (hard rule #7). Any failure — over budget, offline, malformed JSON —
/// returns the candidates untouched so the deterministic templated text always survives.
/// </summary>
public sealed class AnomalyCommentator : IAnomalyCommentator
{
    private const int _charsPerToken = 4;
    private const int _outputTokensPerItem = 80;

    private const string _systemPrompt =
        "You are a Polish personal-finance assistant. You receive detected spending anomalies with "
        + "raw numbers. For each one, write a short, calm, plain-Polish title (max 8 words) and a "
        + "one- or two-sentence description that explains what stands out and why it may be worth a "
        + "look. Do not invent numbers beyond those given, do not give tax or investment advice, and "
        + "do not address the user by name. Return ONLY a JSON array of objects "
        + "{\"index\": int, \"title\": string, \"description\": string}, one per input anomaly, using "
        + "the same index values.";

    private readonly IAiProvider _provider;
    private readonly IAiBudgetGate _budgetGate;
    private readonly IAiUsageLedger _ledger;
    private readonly IAiPricing _pricing;
    private readonly IPromptAnonymizer _anonymizer;
    private readonly ILogger<AnomalyCommentator> _logger;

    public AnomalyCommentator(
        IAiProvider provider,
        IAiBudgetGate budgetGate,
        IAiUsageLedger ledger,
        IAiPricing pricing,
        IPromptAnonymizer anonymizer,
        ILogger<AnomalyCommentator> logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(budgetGate);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(pricing);
        ArgumentNullException.ThrowIfNull(anonymizer);
        ArgumentNullException.ThrowIfNull(logger);
        _provider = provider;
        _budgetGate = budgetGate;
        _ledger = ledger;
        _pricing = pricing;
        _anonymizer = anonymizer;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AnomalyCandidate>> CommentAsync(
        IReadOnlyList<AnomalyCandidate> candidates,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
        {
            return candidates;
        }

        var prompt = BuildPrompt(candidates);
        var model = AiDefaults.ChatModel;

        var estInputTokens = (_systemPrompt.Length + prompt.Length) / _charsPerToken;
        var estOutputTokens = candidates.Count * _outputTokensPerItem;
        var estimate = _pricing.Estimate(model, estInputTokens, estOutputTokens);
        if (!await _budgetGate.CanProceedAsync(estimate.Pln, AiPriority.Normal, ct).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "Budget gate blocked AI commentary for {Count} anomaly candidate(s); keeping templated text.",
                candidates.Count);
            return candidates;
        }

        try
        {
            var request = new AiRequest
            {
                Prompt = prompt,
                Model = model,
                SystemPrompt = _systemPrompt,
                MaxTokens = candidates.Count * _outputTokensPerItem + 128,
                Temperature = 0.4,
            };

            var response = await _provider.CompleteJsonAsync<CommentDto[]>(request, ct).ConfigureAwait(false);
            await _ledger.RecordAsync(response.Usage, AiPurpose.AnomalyComment, ct).ConfigureAwait(false);

            return Merge(candidates, response.Value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "AI commentary failed for {Count} anomaly candidate(s); keeping templated text.",
                candidates.Count);
            return candidates;
        }
    }

    private string BuildPrompt(IReadOnlyList<AnomalyCandidate> candidates)
    {
        var sb = new StringBuilder();
        sb.Append("Anomalies:\n");
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            sb.Append(CultureInfo.InvariantCulture, $"- index {i}: type={c.Type}");
            if (c.RelatedAmount is { } amount)
            {
                sb.Append(CultureInfo.InvariantCulture, $", amount={amount.ToString(CultureInfo.InvariantCulture)} PLN");
            }

            sb.Append(CultureInfo.InvariantCulture, $", period={Iso(c.PeriodFrom)}..{Iso(c.PeriodTo)}");
            foreach (var (key, value) in c.Context)
            {
                sb.Append(CultureInfo.InvariantCulture, $", {key}={value}");
            }

            sb.Append(CultureInfo.InvariantCulture, $"\n  current title: {c.Title}\n  current description: {c.Description}\n");
        }

        return _anonymizer.Anonymize(sb.ToString());
    }

    private static IReadOnlyList<AnomalyCandidate> Merge(
        IReadOnlyList<AnomalyCandidate> candidates,
        CommentDto[]? comments)
    {
        if (comments is null || comments.Length == 0)
        {
            return candidates;
        }

        var byIndex = new Dictionary<int, CommentDto>();
        foreach (var comment in comments)
        {
            byIndex[comment.Index] = comment;
        }

        var merged = new AnomalyCandidate[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            var original = candidates[i];
            if (byIndex.TryGetValue(i, out var comment)
                && !string.IsNullOrWhiteSpace(comment.Title)
                && !string.IsNullOrWhiteSpace(comment.Description))
            {
                merged[i] = original with
                {
                    Title = comment.Title.Trim(),
                    Description = comment.Description.Trim(),
                };
            }
            else
            {
                merged[i] = original;
            }
        }

        return merged;
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record CommentDto(int Index, string? Title, string? Description);
}
