# 07 — Financial Advisor

## Goal

The advisor turns historical transaction data into forward-looking guidance: "can I afford X?", "when can I have Y?", "what would unblock Z?". It supports six goal types and provides AI-mediated suggestions while keeping all calculations deterministic.

## Core principle: deterministic engine, AI commentary

**The engine calculates. The AI explains.**

- All numbers (projections, monthly required savings, status, dates) come from a deterministic C# engine. Testable, reproducible, fast.
- All human-language output (risks, suggestions, scenario descriptions) comes from the LLM, given engine outputs as input.

This split is non-negotiable. LLMs hallucinate numbers; engines don't read context. We use each for what it's good at.

## Goal types

```csharp
public enum GoalType
{
    Purchase,           // furniture, phone, equipment
    LargeExpense,       // vacation, renovation, car
    EmergencyFund,      // 3–6× monthly expenses
    MortgagePrepayment, // overpay home loan principal
    Investment,         // earmark for ETF/bonds (no instrument advice)
    LongTerm            // retirement, child education
}

public enum GoalStatus
{
    OnTrack,
    NeedsAttention,
    AtRisk,
    Late,
    Achieved,
    Paused
}

public enum Priority { Low = 1, Medium = 2, High = 3 }
```

## Domain model

```csharp
public class Goal
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public GoalType Type { get; set; }
    public decimal TargetAmount { get; set; }
    public string Currency { get; set; } = "PLN";
    public DateOnly TargetDate { get; set; }
    public Priority Priority { get; set; }
    public string? Notes { get; set; }                   // free-form context
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<GoalContribution> Contributions { get; set; } = [];
    public List<GoalSnapshot> Snapshots { get; set; } = [];
    public List<Tag> Tags { get; set; } = [];
}

public class GoalContribution
{
    public Guid Id { get; set; }
    public Guid GoalId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public ContributionSource Source { get; set; }       // Manual | LinkedTransaction | Tag | AutoDetect
    public Guid? TransactionId { get; set; }             // if linked
}

public class GoalSnapshot
{
    public Guid Id { get; set; }
    public Guid GoalId { get; set; }
    public DateOnly Date { get; set; }                   // when snapshot was taken
    public decimal CurrentAmount { get; set; }
    public decimal MonthlySaving { get; set; }
    public DateOnly ProjectedDate { get; set; }
    public GoalStatus Status { get; set; }
    public decimal ConfidenceScore { get; set; }         // 0..1
}
```

`GoalSnapshot` is critical — it's the audit log of how the goal's projection changed over time. Lets the UI show "30 days ago this was OnTrack, now it's Late — what changed?".

## Strategy pattern per type

```csharp
public abstract class GoalStrategy
{
    public abstract GoalType Type { get; }
    public abstract GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx);
}

public class FinancialContext
{
    public required decimal MonthlyIncome { get; init; }
    public required decimal MonthlyFixedExpenses { get; init; }
    public required decimal MonthlyVariableAvg { get; init; }     // 6-month moving average
    public required decimal MonthlyVariableStdDev { get; init; }  // for risk modeling
    public required IReadOnlyList<Goal> OtherActiveGoals { get; init; }
    public required IReadOnlyDictionary<string, decimal> CategoryAverages6m { get; init; }
    public required IReadOnlyDictionary<DateOnly, decimal> SeasonalityModifiers { get; init; }
    public required DateOnly Today { get; init; }
}

public class GoalFeasibilityResult
{
    public required GoalStatus Status { get; init; }
    public required DateOnly ProjectedDate { get; init; }
    public required decimal RequiredMonthlySaving { get; init; }
    public required decimal CurrentMonthlySaving { get; init; }
    public required decimal ConfidenceScore { get; init; }        // 0..1
    public required List<Scenario> AlternativeScenarios { get; init; }
    public required List<RiskFactor> Risks { get; init; }
    public required string DiagnosticSummary { get; init; }       // for LLM input, not user-facing
}
```

### PurchaseGoalStrategy

For a one-time defined amount with a target date.

```csharp
public class PurchaseGoalStrategy : GoalStrategy
{
    public override GoalType Type => GoalType.Purchase;

    public override GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx)
    {
        var saved = goal.Contributions.Sum(c => c.Amount);
        var remaining = Math.Max(0, goal.TargetAmount - saved);

        var monthsAvailable = MonthsBetween(ctx.Today, goal.TargetDate);
        if (monthsAvailable <= 0)
            return ResultWithStatus(GoalStatus.Late, ctx.Today, ...);

        var required = remaining / monthsAvailable;
        var freeCash = ctx.MonthlyIncome - ctx.MonthlyFixedExpenses - ctx.MonthlyVariableAvg
                       - ctx.OtherActiveGoals.Sum(EstimateMonthlyContribution);

        var status = required <= freeCash * 0.5m ? GoalStatus.OnTrack
                   : required <= freeCash         ? GoalStatus.NeedsAttention
                   :                                GoalStatus.AtRisk;

        var projected = required <= freeCash
            ? goal.TargetDate
            : ctx.Today.AddMonths((int)Math.Ceiling((double)(remaining / Math.Max(100m, freeCash * 0.7m))));

        return new GoalFeasibilityResult
        {
            Status = status,
            ProjectedDate = projected,
            RequiredMonthlySaving = required,
            CurrentMonthlySaving = EstimateRecentMonthlyContribution(goal),
            ConfidenceScore = ComputeConfidence(ctx, monthsAvailable),
            AlternativeScenarios = BuildScenarios(goal, ctx, required, freeCash),
            Risks = BuildRisks(ctx, goal),
            DiagnosticSummary = $"target={goal.TargetAmount} saved={saved} req/mo={required:F2} free={freeCash:F2}"
        };
    }
}
```

### LargeExpenseStrategy

Like Purchase but typically larger and benefits from seasonality awareness (vacation in summer; you've spent more on vacations in the past, expect a similar pattern).

Uses `ctx.SeasonalityModifiers` to predict that "you save less in December because of holiday expenses" → projection adjusts.

### EmergencyFundStrategy

Different goal mechanic. Target is a multiple of monthly expenses (default 6×), not a fixed amount.

```csharp
public override GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx)
{
    var multiple = goal.TargetAmount / Math.Max(1, ctx.MonthlyFixedExpenses + ctx.MonthlyVariableAvg);
    // ... target naturally grows with expenses, so re-evaluate target each snapshot
}
```

When monthly expenses grow, the target grows. UI shows "Cel: 6× miesięczne wydatki = 18 000 zł (zaktualizowane na podstawie ostatnich 6 mies.)".

### MortgagePrepaymentStrategy

Two sub-modes: shorten loan duration vs reduce monthly payment. Calculator uses standard amortization formulas:

```csharp
public class MortgagePrepaymentCalculator
{
    public PrepaymentEffect Calculate(
        decimal principalRemaining,
        decimal annualRate,
        int monthsRemaining,
        decimal prepayment,
        PrepaymentMode mode)
    {
        // Standard amortization math with extra principal payment
    }
}
```

Outputs:
- Interest saved over remaining loan
- New payment amount (if reduce mode) or new payoff date (if shorten mode)
- Break-even time

The strategy itself does NOT recommend shorten vs reduce — it shows both and the user chooses. Mention prevention of "advisor that recommends investment" pitfall.

### InvestmentStrategy

This strategy is **deliberately limited** to avoid licensed-advice territory.

What it DOES:
- Calculates monthly free cash that could go toward investing
- Tracks committed amount vs invested amount
- Shows comparison to inflation (as opportunity cost of cash)

What it DOES NOT:
- Recommend specific instruments (stocks, bonds, ETFs)
- Predict returns
- Suggest rebalancing

UI for this goal type shows a warning banner with links to research tools (`obligacjeskarbowe.pl`, `KNF` register of advisors).

### LongTermStrategy

For 5+ year horizons. Adds inflation modeling:

- User specifies expected nominal target (e.g., 50,000 PLN)
- Strategy shows real value at target date assuming X% annual inflation
- Suggests inflation-adjusted contribution increase

Again, no investment advice. The advisor says "you'll need 50k nominal, but real value will be ~33k assuming 4% inflation" without recommending how to invest.

## The engine — orchestration

```csharp
public class GoalFeasibilityEngine
{
    private readonly IReadOnlyDictionary<GoalType, GoalStrategy> _strategies;

    public GoalFeasibilityResult Evaluate(Goal goal, FinancialContext ctx) =>
        _strategies[goal.Type].Evaluate(goal, ctx);

    public IReadOnlyList<GoalFeasibilityResult> EvaluateAll(
        IReadOnlyList<Goal> goals,
        FinancialContext ctx)
    {
        // Important: ctx.OtherActiveGoals is set per goal (excluding self)
        return goals
            .Where(g => !g.IsArchived)
            .Select(g => Evaluate(g, ctx with { OtherActiveGoals = goals.Where(o => o.Id != g.Id).ToList() }))
            .ToList();
    }
}
```

Run by a background service every day at startup → produces daily `GoalSnapshot` rows. UI compares today's snapshot to last week's.

## AI layer — risks and suggestions

The engine outputs are fed to an LLM along with diagnostic metadata. Prompt template:

```
You are a financial advisor for a Polish user. The user has goals; below are deterministic calculations and historical context.

Your job:
1. Identify 0–2 specific risks for each goal in 1 short Polish sentence each
2. Identify 0–3 actionable suggestions for the user's overall financial picture, each with an estimated PLN/month savings amount
3. Each suggestion must be tied to a category in the user's history with a clear comparison ("X above 6-month average")

Constraints:
- Numbers come from the engine; do NOT invent figures
- Always include the source of each number ("based on category 'Restauracje', avg vs current")
- Politely decline if asked for tax or investment recommendations

Engine outputs:
{goalsResults}

Recent context:
- Income: {monthlyIncome}
- Fixed expenses: {monthlyFixed}
- Variable expenses (6m avg): {variableAvg}
- Top 3 categories above their 6m average:
  - {cat1}: +{delta1} PLN ({pct1}%)
  - {cat2}: +{delta2} PLN ({pct2}%)
  - {cat3}: +{delta3} PLN ({pct3}%)

Return JSON: {
  "perGoalRisks": { "{goalId}": ["risk text"] },
  "suggestions": [
    { "title": string, "savings": number, "description": string, "categoryAffected": string }
  ]
}
```

Result is stored in `AdvisorReport` table for the day. UI renders it; LLM is not called every UI refresh.

## Aggressiveness profile

User-selectable: Conservative / Balanced / Aggressive. Default: Balanced. The profile affects:

- Free-cash calculation (Conservative leaves a 20% buffer; Aggressive uses 95%)
- Target stretch (Conservative pushes dates outward by 10%; Aggressive accepts tight targets)
- Risk tolerance (Conservative flags goals as `AtRisk` sooner)

Implementation: a multiplier set passed into each strategy.

## Cutting suggestions — the "wyrzeczenia" feature

Generated by AI but grounded in engine numbers. Always cite a category and a comparison, never general "spend less".

Examples generated for our test data:
- "Restauracje do średniej 6 mies. → +329 zł/mies. (z 540 do 211 zł)"
- "Audyt subskrypcji → +128 zł/mies. (3 z 4 nieużywane przez ostatni miesiąc)"
- "Steam — limit 50 zł/mies. → +79 zł/mies. (4-mies. trend rosnący)"

Each suggestion is a card the user can "apply", which:
- Creates a `Rule` to track the category going forward
- Adjusts goal projections immediately (assumed contribution to top-priority goal)
- The user can revert any time

## Goal-transaction linkage

Three ways a transaction contributes to a goal:

1. **Manual:** user opens transaction, clicks "Add to goal X"
2. **Tag-based:** transaction tagged `goal:vacation-2026` automatically credits Vacation goal
3. **Auto-detect:** transfer to a savings sub-account known to be associated with a goal

Tag approach is the most flexible — see `02-database-and-encryption.md` for tag model.

## Testing the engine

The engine is purely deterministic, so unit tests are straightforward but extensive:

```csharp
[Theory]
[InlineData(3000, 1200, 6, 300, 300, GoalStatus.OnTrack)]
[InlineData(3000, 1200, 3, 600, 200, GoalStatus.AtRisk)]
[InlineData(50000, 0, 12, 4167, 1000, GoalStatus.Late)]
public void PurchaseGoal_ProducesCorrectStatus(
    decimal target, decimal saved, int monthsAvail,
    decimal required, decimal current, GoalStatus expected)
{
    // Arrange: build goal, ctx
    // Act: evaluate
    // Assert: result.Status == expected
}
```

Property-based tests:
- Saved amount monotonic → projected date never moves backward (without contribution removal)
- Sum of feasibility per goal cannot exceed user's free cash by more than a defined ratio
- Archived goals are not evaluated

## What this advisor never does

- Predict returns on investments
- Recommend specific stocks, bonds, ETFs, banks, or insurance products
- Calculate or file taxes
- Make decisions for the user (always shows options, user picks)
- Generate suggestions without grounding in actual user data
