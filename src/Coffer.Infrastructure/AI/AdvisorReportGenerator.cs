using System.Globalization;
using System.Text;
using Coffer.Core.Ai;
using Coffer.Core.Domain;
using Coffer.Core.Goals;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// 14-C advisor commentary: turns the engine's <see cref="GoalFeasibilityResult"/>s into a day's
/// <see cref="AdvisorReport"/> — per-goal risk sentences and grounded cutting suggestions
/// (doc 07, "the engine calculates, the AI explains"). One provider call per run on
/// <see cref="AiDefaults.ChatModel"/>, gated by <see cref="IAiBudgetGate"/> at
/// <see cref="AiPriority.Normal"/> and metered once as <see cref="AiPurpose.AdvisorReport"/>. The
/// prompt is anonymised (hard rule #7) and carries only goal ids + engine numbers, never goal
/// names. Any failure — over budget, offline, malformed JSON — yields an engine-only report so the
/// advisor always has the deterministic risk text to show.
/// </summary>
public sealed class AdvisorReportGenerator : IAdvisorReportGenerator
{
    private const int CharsPerToken = 4;
    private const int OutputTokensPerGoal = 70;
    private const int OutputTokensForSuggestions = 240;
    private const int TopCategoryCount = 3;

    private const string SystemPrompt =
        "You are a financial advisor for a Polish user. You receive deterministic engine calculations "
        + "for the user's savings goals plus recent spending context. Your job: (1) for each goal, "
        + "write 0-2 specific risks as one short Polish sentence each; (2) propose 0-3 actionable "
        + "cutting suggestions for the overall picture, each with an estimated PLN/month saving and "
        + "tied to a spending category with a clear comparison (e.g. 'above the 6-month average'). "
        + "Constraints: numbers come from the engine and the category context — do NOT invent figures; "
        + "always cite the source category for a suggestion; politely decline any tax or investment "
        + "recommendation; do not address the user by name. Return ONLY JSON of the form "
        + "{\"perGoalRisks\": {\"<goalId>\": [\"risk text\"]}, \"suggestions\": "
        + "[{\"title\": string, \"savings\": number, \"description\": string, \"categoryAffected\": string}]}, "
        + "keying perGoalRisks by the exact goal id values given.";

    private readonly IAiProvider _provider;
    private readonly IAiBudgetGate _budgetGate;
    private readonly IAiUsageLedger _ledger;
    private readonly IAiPricing _pricing;
    private readonly IPromptAnonymizer _anonymizer;
    private readonly ILogger<AdvisorReportGenerator> _logger;

    public AdvisorReportGenerator(
        IAiProvider provider,
        IAiBudgetGate budgetGate,
        IAiUsageLedger ledger,
        IAiPricing pricing,
        IPromptAnonymizer anonymizer,
        ILogger<AdvisorReportGenerator> logger)
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

    public async Task<AdvisorReport> GenerateAsync(
        IReadOnlyList<GoalFeasibilityResult> results,
        FinancialContext context,
        IReadOnlyList<CategorySpending> categorySpending,
        DateOnly date,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(categorySpending);

        if (results.Count == 0)
        {
            return EngineOnlyReport(results, date);
        }

        var topCategories = categorySpending
            .Where(c => c.Delta > 0m)
            .OrderByDescending(c => c.Delta)
            .Take(TopCategoryCount)
            .ToList();

        var prompt = BuildPrompt(results, context, topCategories);
        var model = AiDefaults.ChatModel;

        var estInputTokens = (SystemPrompt.Length + prompt.Length) / CharsPerToken;
        var estOutputTokens = (results.Count * OutputTokensPerGoal) + OutputTokensForSuggestions;
        var estimate = _pricing.Estimate(model, estInputTokens, estOutputTokens);
        if (!await _budgetGate.CanProceedAsync(estimate.Pln, AiPriority.Normal, ct).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "Budget gate blocked the advisor report for {Count} goal(s); using engine-only risks.",
                results.Count);
            return EngineOnlyReport(results, date);
        }

        try
        {
            var request = new AiRequest
            {
                Prompt = prompt,
                Model = model,
                SystemPrompt = SystemPrompt,
                MaxTokens = estOutputTokens + 256,
                Temperature = 0.4,
            };

            var response = await _provider.CompleteJsonAsync<ReportDto>(request, ct).ConfigureAwait(false);
            await _ledger.RecordAsync(response.Usage, AiPurpose.AdvisorReport, ct).ConfigureAwait(false);

            return BuildReport(results, response.Value, date);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Advisor report generation failed for {Count} goal(s); using engine-only risks.",
                results.Count);
            return EngineOnlyReport(results, date);
        }
    }

    private string BuildPrompt(
        IReadOnlyList<GoalFeasibilityResult> results,
        FinancialContext context,
        IReadOnlyList<CategorySpending> topCategories)
    {
        var sb = new StringBuilder();
        sb.Append("Engine outputs:\n");
        foreach (var r in results)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"- goal {r.GoalId}: status={r.Status}, effectiveTarget={Money(r.EffectiveTarget)} PLN, "
                + $"projectedDate={Iso(r.ProjectedDate)}, required={Money(r.RequiredMonthlySaving)} PLN/mo, "
                + $"current={Money(r.CurrentMonthlySaving)} PLN/mo, confidence={r.ConfidenceScore.ToString("0.00", CultureInfo.InvariantCulture)}\n");
            foreach (var risk in r.Risks)
            {
                sb.Append(CultureInfo.InvariantCulture, $"    risk {risk.Code}: {risk.Description}\n");
            }
        }

        sb.Append("\nRecent context:\n");
        sb.Append(CultureInfo.InvariantCulture, $"- Income: {Money(context.MonthlyIncome)} PLN\n");
        sb.Append(CultureInfo.InvariantCulture, $"- Fixed expenses: {Money(context.MonthlyFixedExpenses)} PLN\n");
        sb.Append(CultureInfo.InvariantCulture, $"- Variable expenses (6m avg): {Money(context.MonthlyVariableAvg)} PLN\n");

        if (topCategories.Count > 0)
        {
            sb.Append("- Top categories above their 6m average:\n");
            foreach (var c in topCategories)
            {
                var pct = c.Average6m > 0m ? c.Delta / c.Average6m * 100m : 0m;
                sb.Append(CultureInfo.InvariantCulture,
                    $"  - {c.Category}: current {Money(c.Current)} PLN vs avg {Money(c.Average6m)} PLN, "
                    + $"+{Money(c.Delta)} PLN ({pct.ToString("0", CultureInfo.InvariantCulture)}%)\n");
            }
        }
        else
        {
            sb.Append("- No categories are currently above their 6m average.\n");
        }

        return _anonymizer.Anonymize(sb.ToString());
    }

    private static AdvisorReport BuildReport(
        IReadOnlyList<GoalFeasibilityResult> results,
        ReportDto? dto,
        DateOnly date)
    {
        if (dto is null)
        {
            return EngineOnlyReport(results, date);
        }

        var reportId = Guid.NewGuid();
        var entries = new List<AdvisorSuggestion>();
        var knownGoals = results.Select(r => r.GoalId).ToHashSet();

        if (dto.PerGoalRisks is not null)
        {
            foreach (var (goalIdText, risks) in dto.PerGoalRisks)
            {
                if (risks is null
                    || !Guid.TryParse(goalIdText, out var goalId)
                    || !knownGoals.Contains(goalId))
                {
                    continue;
                }

                foreach (var risk in risks)
                {
                    if (string.IsNullOrWhiteSpace(risk))
                    {
                        continue;
                    }

                    entries.Add(new AdvisorSuggestion
                    {
                        Id = Guid.NewGuid(),
                        ReportId = reportId,
                        Kind = AdvisorEntryKind.Risk,
                        GoalId = goalId,
                        Description = risk.Trim(),
                    });
                }
            }
        }

        if (dto.Suggestions is not null)
        {
            foreach (var s in dto.Suggestions)
            {
                if (string.IsNullOrWhiteSpace(s.Title) || string.IsNullOrWhiteSpace(s.Description))
                {
                    continue;
                }

                entries.Add(new AdvisorSuggestion
                {
                    Id = Guid.NewGuid(),
                    ReportId = reportId,
                    Kind = AdvisorEntryKind.Suggestion,
                    Title = s.Title.Trim(),
                    Savings = s.Savings,
                    Description = s.Description.Trim(),
                    CategoryAffected = string.IsNullOrWhiteSpace(s.CategoryAffected) ? null : s.CategoryAffected.Trim(),
                });
            }
        }

        return new AdvisorReport
        {
            Id = reportId,
            Date = date,
            GeneratedAt = DateTime.UtcNow,
            GeneratedByAi = true,
            Entries = entries,
        };
    }

    private static AdvisorReport EngineOnlyReport(IReadOnlyList<GoalFeasibilityResult> results, DateOnly date)
    {
        var reportId = Guid.NewGuid();
        var entries = new List<AdvisorSuggestion>();
        foreach (var r in results)
        {
            foreach (var risk in r.Risks)
            {
                entries.Add(new AdvisorSuggestion
                {
                    Id = Guid.NewGuid(),
                    ReportId = reportId,
                    Kind = AdvisorEntryKind.Risk,
                    GoalId = r.GoalId,
                    Description = risk.Description,
                });
            }
        }

        return new AdvisorReport
        {
            Id = reportId,
            Date = date,
            GeneratedAt = DateTime.UtcNow,
            GeneratedByAi = false,
            Entries = entries,
        };
    }

    private static string Money(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record ReportDto
    {
        public Dictionary<string, List<string>?>? PerGoalRisks { get; init; }

        public SuggestionDto[]? Suggestions { get; init; }
    }

    private sealed record SuggestionDto(string? Title, decimal Savings, string? Description, string? CategoryAffected);
}
