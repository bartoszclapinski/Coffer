using System.Text.Json;
using Coffer.Core.Goals;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Chat;

/// <summary>
/// Read-only chat tool that returns the user's active savings goals with the engine's current
/// projection for each — status, effective target, target and projected dates, and the required vs
/// current monthly saving (doc 07). The numbers come from the deterministic
/// <see cref="IGoalFeasibilityEngine"/>, evaluated live against the financial context, so the
/// assistant answers "how is my holiday goal going?" with the engine's figures rather than inventing
/// any. Realises the <c>GetGoals</c> tool deferred in Sprint 12.
/// </summary>
public sealed class GetGoalsTool : ChatTool
{
    private readonly IGoalsQuery _goals;
    private readonly IFinancialContextBuilder _contextBuilder;
    private readonly IGoalFeasibilityEngine _engine;

    public GetGoalsTool(
        IDbContextFactory<CofferDbContext> contextFactory,
        IGoalsQuery goals,
        IFinancialContextBuilder contextBuilder,
        IGoalFeasibilityEngine engine)
        : base(contextFactory)
    {
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(contextBuilder);
        ArgumentNullException.ThrowIfNull(engine);
        _goals = goals;
        _contextBuilder = contextBuilder;
        _engine = engine;
    }

    public override string Name => "GetGoals";

    public override string Description =>
        "Aktywne cele oszczędnościowe użytkownika wraz z aktualną prognozą silnika: status, kwota "
        + "docelowa, data docelowa, prognozowana data osiągnięcia oraz wymagana i bieżąca miesięczna "
        + "oszczędność. Bez parametrów. Kwoty w PLN, daty w formacie RRRR-MM-DD.";

    public override string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {}
        }
        """;

    private protected override async Task<object> RunAsync(JsonElement args, CofferDbContext db, CancellationToken ct)
    {
        var goals = await _goals.GetActiveAsync(ct).ConfigureAwait(false);
        if (goals.Count == 0)
        {
            return new { count = 0, goals = Array.Empty<object>() };
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var context = await _contextBuilder.BuildAsync(today, ct).ConfigureAwait(false);
        var results = _engine.EvaluateAll(goals, context);
        var byId = goals.ToDictionary(g => g.Id);

        var projected = results
            .Select(r =>
            {
                var goal = byId[r.GoalId];
                return new
                {
                    name = goal.Name,
                    type = goal.Type.ToString(),
                    status = r.Status.ToString(),
                    target = r.EffectiveTarget,
                    currency = goal.Currency,
                    targetDate = Iso(goal.TargetDate),
                    projectedDate = Iso(r.ProjectedDate),
                    requiredMonthlySaving = r.RequiredMonthlySaving,
                    currentMonthlySaving = r.CurrentMonthlySaving,
                    confidence = r.ConfidenceScore,
                };
            })
            .ToList();

        return new { count = projected.Count, goals = projected };
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");
}
