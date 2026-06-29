using Coffer.Core.Domain;

namespace Coffer.Core.Goals;

/// <summary>
/// One feasibility strategy per <see cref="GoalType"/> (doc 07, strategy pattern). The base
/// provides the shared deterministic primitives — months math, free-cash apportioning, status
/// thresholds, projection, confidence — so each concrete strategy expresses only what is specific
/// to its goal mechanic. Every helper is pure: same inputs ⇒ same outputs (the engine calculates;
/// the AI explains). Money is <c>decimal</c> (hard rule #1).
/// </summary>
public abstract class GoalStrategy
{
    public abstract GoalType Type { get; }

    public abstract GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx);

    /// <summary>Whole calendar months from <paramref name="from"/> to <paramref name="to"/>, never negative.</summary>
    protected static int WholeMonthsBetween(DateOnly from, DateOnly to)
    {
        if (to <= from)
        {
            return 0;
        }

        var months = ((to.Year - from.Year) * 12) + (to.Month - from.Month);
        if (to.Day < from.Day)
        {
            months--;
        }

        return Math.Max(0, months);
    }

    protected static decimal Saved(Goal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return goal.Contributions.Sum(c => c.Amount);
    }

    /// <summary>Raw monthly free cash after fixed + variable spend and the pull of other active goals.</summary>
    protected static decimal RawFreeCash(FinancialContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return ctx.MonthlyIncome
            - ctx.MonthlyFixedExpenses
            - ctx.MonthlyVariableAvg
            - ctx.OtherActiveGoals.Sum(g => EstimateMonthlyContribution(g, ctx.Today));
    }

    /// <summary>Free cash the active profile is willing to commit toward a goal (never negative).</summary>
    protected static decimal UsableFreeCash(FinancialContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Math.Max(0m, RawFreeCash(ctx)) * ctx.Profile.FreeCashUtilisation;
    }

    /// <summary>A straight-line estimate of what a goal needs per month to hit its target date.</summary>
    protected static decimal EstimateMonthlyContribution(Goal goal, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(goal);
        var remaining = Math.Max(0m, goal.TargetAmount - Saved(goal));
        var months = WholeMonthsBetween(today, goal.TargetDate);
        return months <= 0 ? remaining : remaining / months;
    }

    /// <summary>Recent saving pace: contributions in the last three months, per month.</summary>
    protected static decimal RecentMonthlyContribution(Goal goal, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(goal);
        var since = today.AddMonths(-3);
        var recent = goal.Contributions.Where(c => c.Date > since).Sum(c => c.Amount);
        return recent / 3m;
    }

    protected static GoalStatus StatusFor(decimal required, decimal usableFreeCash, AggressivenessProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (required <= usableFreeCash * profile.OnTrackHeadroom)
        {
            return GoalStatus.OnTrack;
        }

        return required <= usableFreeCash ? GoalStatus.NeedsAttention : GoalStatus.AtRisk;
    }

    /// <summary>The date a remaining balance is reached at a given monthly pace (far future if pace ≤ 0).</summary>
    protected static DateOnly ProjectDateAtPace(DateOnly today, decimal remaining, decimal monthlySaving)
    {
        if (remaining <= 0m)
        {
            return today;
        }

        if (monthlySaving <= 0m)
        {
            return DateOnly.MaxValue;
        }

        // A near-zero pace can imply astronomically many months; past ~1000 years the answer is
        // "effectively never", and DateOnly.AddMonths would overflow anyway.
        var rawMonths = Math.Ceiling(remaining / monthlySaving);
        if (rawMonths > 12_000m)
        {
            return DateOnly.MaxValue;
        }

        return today.AddMonths((int)rawMonths);
    }

    /// <summary>Confidence 0..1: higher when spending is steady and the required saving leaves slack.</summary>
    protected static decimal Confidence(FinancialContext ctx, decimal required, decimal usableFreeCash)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var variability = ctx.MonthlyVariableAvg <= 0m
            ? 0m
            : ctx.MonthlyVariableStdDev / ctx.MonthlyVariableAvg;
        var tightness = usableFreeCash <= 0m ? 1m : Math.Min(1m, required / usableFreeCash);
        var raw = 1m - (Math.Min(1m, variability) * 0.5m) - (tightness * 0.4m);
        return Clamp(raw, 0.05m, 0.98m);
    }

    protected static decimal Clamp(decimal value, decimal min, decimal max) =>
        value < min ? min : value > max ? max : value;

    /// <summary>
    /// The shared straight-line feasibility most goals reduce to: how much must be set aside each
    /// month to reach <paramref name="targetAmount"/> by the goal's date, whether the active profile
    /// can sustain it, and what the projection and risks look like. Concrete strategies that only
    /// differ in how the target is derived (e.g. an emergency fund sized off monthly expenses) call
    /// this with their target; strategies with bespoke mechanics override <see cref="Evaluate"/>.
    /// </summary>
    protected GoalFeasibilityResult EvaluateSavingsGoal(
        Goal goal,
        FinancialContext ctx,
        decimal targetAmount,
        IReadOnlyList<RiskFactor>? extraRisks = null)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(ctx);

        var saved = Saved(goal);
        var remaining = Math.Max(0m, targetAmount - saved);
        var months = WholeMonthsBetween(ctx.Today, goal.TargetDate);
        var required = months <= 0 ? remaining : remaining / months;
        var usable = UsableFreeCash(ctx);
        var current = RecentMonthlyContribution(goal, ctx.Today);

        var status = remaining <= 0m
            ? GoalStatus.Achieved
            : months <= 0
                ? GoalStatus.Late
                : StatusFor(required, usable, ctx.Profile);

        var projectionPace = Math.Max(current, usable);
        var projectedDate = remaining <= 0m
            ? ctx.Today
            : ProjectDateAtPace(ctx.Today, remaining, projectionPace);

        var scenarios = BuildScenarios(ctx, goal.TargetDate, remaining, required, usable, current);
        var risks = BuildRisks(ctx, required, usable, months, remaining);
        if (extraRisks is { Count: > 0 })
        {
            risks = [.. risks, .. extraRisks];
        }

        var summary =
            $"{Type}: saved {saved:0.##}/{targetAmount:0.##}, remaining {remaining:0.##} over " +
            $"{months} mo ⇒ required {required:0.##}/mo vs usable {usable:0.##}/mo; status {status}.";

        return new GoalFeasibilityResult
        {
            GoalId = goal.Id,
            Status = status,
            EffectiveTarget = targetAmount,
            ProjectedDate = projectedDate,
            RequiredMonthlySaving = required,
            CurrentMonthlySaving = current,
            ConfidenceScore = Confidence(ctx, required, usable),
            AlternativeScenarios = scenarios,
            Risks = risks,
            DiagnosticSummary = summary,
        };
    }

    /// <summary>
    /// The target this strategy evaluates against. Most goals use the stored amount; an emergency
    /// fund overrides this to size itself off current expenses, so the simulator and chart cap track
    /// the same target the verdict used.
    /// </summary>
    public virtual decimal ResolveTargetAmount(Goal goal, FinancialContext ctx)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return goal.TargetAmount;
    }

    /// <summary>
    /// A deterministic "what if I save this much per month" outcome for the simulator slider: the
    /// engine projects the payoff date and status at <paramref name="monthlySaving"/> against the
    /// strategy's effective target. Pure — the same inputs always yield the same scenario.
    /// </summary>
    public Scenario Simulate(Goal goal, FinancialContext ctx, decimal monthlySaving)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(ctx);

        var target = ResolveTargetAmount(goal, ctx);
        var saved = Saved(goal);
        var remaining = Math.Max(0m, target - saved);
        var months = WholeMonthsBetween(ctx.Today, goal.TargetDate);
        var required = months <= 0 ? remaining : remaining / months;

        if (remaining <= 0m)
        {
            return new Scenario("SIMULATION", monthlySaving, ctx.Today, GoalStatus.Achieved);
        }

        return new Scenario(
            "SIMULATION",
            monthlySaving,
            ProjectDateAtPace(ctx.Today, remaining, monthlySaving),
            StatusFor(required, monthlySaving, ctx.Profile));
    }

    /// <summary>Three deterministic what-ifs: current pace, the most the profile sustains, and the pace that hits the date.</summary>
    private IReadOnlyList<Scenario> BuildScenarios(
        FinancialContext ctx,
        DateOnly targetDate,
        decimal remaining,
        decimal required,
        decimal usable,
        decimal current)
    {
        return
        [
            new Scenario(
                "CURRENT_PACE",
                current,
                ProjectDateAtPace(ctx.Today, remaining, current),
                StatusFor(required, current, ctx.Profile)),
            new Scenario(
                "MAX_SUSTAINABLE",
                usable,
                ProjectDateAtPace(ctx.Today, remaining, usable),
                StatusFor(required, usable, ctx.Profile)),
            new Scenario(
                "ON_TARGET",
                required,
                targetDate,
                GoalStatus.OnTrack),
        ];
    }

    private static IReadOnlyList<RiskFactor> BuildRisks(
        FinancialContext ctx,
        decimal required,
        decimal usable,
        int months,
        decimal remaining)
    {
        var risks = new List<RiskFactor>();

        if (RawFreeCash(ctx) <= 0m)
        {
            risks.Add(new RiskFactor("NO_FREE_CASH", "Income minus committed spend and other goals leaves no free cash."));
        }
        else if (required > usable)
        {
            risks.Add(new RiskFactor("INSUFFICIENT_FREE_CASH", "Required monthly saving exceeds the free cash the profile commits."));
        }

        if (months <= 0 && remaining > 0m)
        {
            risks.Add(new RiskFactor("PAST_TARGET_DATE", "The target date has passed with money still to save."));
        }

        var variability = ctx.MonthlyVariableAvg <= 0m
            ? 0m
            : ctx.MonthlyVariableStdDev / ctx.MonthlyVariableAvg;
        if (variability > 0.3m)
        {
            risks.Add(new RiskFactor("VOLATILE_SPENDING", "Variable spending swings widely month to month, so free cash is unpredictable."));
        }

        return risks;
    }
}
