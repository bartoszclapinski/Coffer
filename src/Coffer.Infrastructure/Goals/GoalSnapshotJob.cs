using Coffer.Core.Domain;
using Coffer.Core.Goals;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Goals;

/// <summary>
/// The daily advisor refresh (doc 07). Runs the deterministic <see cref="IGoalFeasibilityEngine"/>
/// over the active goals, persists one <see cref="GoalSnapshot"/> per goal for the day, and
/// regenerates the day's <see cref="AdvisorReport"/> through the budget-gated
/// <see cref="IAdvisorReportGenerator"/> — the single place the LLM is invoked, so UI refreshes stay
/// free. Idempotent within a day: if the day already has snapshots the run is a no-op, which lets the
/// desktop startup task fire it on every launch without piling up duplicates.
/// </summary>
public sealed class GoalSnapshotJob : IGoalSnapshotJob
{
    private const string Currency = "PLN";
    private const string UncategorizedName = "Bez kategorii";

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;
    private readonly IGoalsQuery _goals;
    private readonly IFinancialContextBuilder _contextBuilder;
    private readonly IGoalFeasibilityEngine _engine;
    private readonly IAdvisorReportGenerator _reportGenerator;
    private readonly ILogger<GoalSnapshotJob> _logger;

    public GoalSnapshotJob(
        IDbContextFactory<CofferDbContext> contextFactory,
        IGoalsQuery goals,
        IFinancialContextBuilder contextBuilder,
        IGoalFeasibilityEngine engine,
        IAdvisorReportGenerator reportGenerator,
        ILogger<GoalSnapshotJob> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(contextBuilder);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(reportGenerator);
        ArgumentNullException.ThrowIfNull(logger);
        _contextFactory = contextFactory;
        _goals = goals;
        _contextBuilder = contextBuilder;
        _engine = engine;
        _reportGenerator = reportGenerator;
        _logger = logger;
    }

    public async Task<int> RunAsync(DateOnly today, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Idempotent within a day: snapshots present for today means the job already ran.
        if (await db.GoalSnapshots.AsNoTracking().AnyAsync(s => s.Date == today, ct).ConfigureAwait(false))
        {
            return 0;
        }

        var goals = await _goals.GetActiveAsync(ct).ConfigureAwait(false);
        if (goals.Count == 0)
        {
            return 0;
        }

        var context = await _contextBuilder.BuildAsync(today, ct).ConfigureAwait(false);
        var results = _engine.EvaluateAll(goals, context);

        var contributionsByGoal = goals.ToDictionary(
            g => g.Id,
            g => g.Contributions.Sum(c => c.Amount));

        foreach (var result in results)
        {
            db.GoalSnapshots.Add(new GoalSnapshot
            {
                Id = Guid.NewGuid(),
                GoalId = result.GoalId,
                Date = today,
                CurrentAmount = contributionsByGoal.GetValueOrDefault(result.GoalId),
                MonthlySaving = result.CurrentMonthlySaving,
                ProjectedDate = result.ProjectedDate,
                Status = result.Status,
                ConfidenceScore = result.ConfidenceScore,
            });
        }

        var categorySpending = await BuildCategorySpendingAsync(db, context, ct).ConfigureAwait(false);
        var report = await _reportGenerator
            .GenerateAsync(results, context, categorySpending, today, ct)
            .ConfigureAwait(false);

        // Replace any earlier report for the day so a re-run after a crash never duplicates the date.
        var existing = await db.AdvisorReports
            .Where(r => r.Date == today)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (existing.Count > 0)
        {
            db.AdvisorReports.RemoveRange(existing);
        }

        db.AdvisorReports.Add(report);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Goal snapshot job wrote {Count} snapshot(s) and an advisor report (ai={Ai}) for {Date}.",
            results.Count, report.GeneratedByAi, today);

        return results.Count;
    }

    private static async Task<IReadOnlyList<CategorySpending>> BuildCategorySpendingAsync(
        CofferDbContext db,
        FinancialContext context,
        CancellationToken ct)
    {
        var scope = db.Transactions.AsNoTracking().Where(t => t.Currency == Currency);
        if (!await scope.AnyAsync(ct).ConfigureAwait(false))
        {
            return [];
        }

        var anchor = await scope.MaxAsync(t => t.Date, ct).ConfigureAwait(false);
        var monthStart = new DateOnly(anchor.Year, anchor.Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var currentDebits = await scope
            .Where(t => t.Date >= monthStart && t.Date < monthEnd && t.Amount < 0m)
            .GroupBy(t => t.CategoryId)
            .Select(g => new { g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var categoryNames = await db.Categories.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct)
            .ConfigureAwait(false);

        var spending = new List<CategorySpending>();
        foreach (var row in currentDebits)
        {
            var name = row.Key is { } id && categoryNames.TryGetValue(id, out var n) ? n : UncategorizedName;
            var current = -row.Total;
            var average = context.CategoryAverages6m.GetValueOrDefault(name);
            spending.Add(new CategorySpending(name, current, average));
        }

        return spending;
    }
}
