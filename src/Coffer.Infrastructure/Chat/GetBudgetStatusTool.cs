using System.Text.Json;
using Coffer.Core.Budgeting;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Chat;

/// <summary>
/// Read-only chat tool that reports the current month's category budgets: for each budgeted category the
/// limit, month-to-date spend, remaining, the linear end-of-month projection, and the ok/approaching/over
/// zone — plus the unbudgeted lines (categories with spend but no limit, including the uncategorised
/// bucket) so overspending is never hidden. The numbers come from the deterministic
/// <see cref="BudgetTrackingEngine"/> via <see cref="IBudgetTrackingQuery"/>, so the assistant answers
/// "am I over budget this month?" with the engine's figures rather than inventing any. Parameterless like
/// <c>GetGoals</c>; the month is the dashboard-anchored current month. Realises the <c>GetBudgetStatus</c>
/// tool deferred as Sprint-20's optional 20-C.
/// </summary>
public sealed class GetBudgetStatusTool : ChatTool
{
    private readonly IBudgetTrackingQuery _budgetTracking;

    public GetBudgetStatusTool(
        IDbContextFactory<CofferDbContext> contextFactory,
        IBudgetTrackingQuery budgetTracking)
        : base(contextFactory)
    {
        ArgumentNullException.ThrowIfNull(budgetTracking);
        _budgetTracking = budgetTracking;
    }

    public override string Name => "GetBudgetStatus";

    public override string Description =>
        "Stan miesięcznych budżetów kategorii: dla każdej kategorii z ustawionym limitem — limit, "
        + "wydatki od początku miesiąca, pozostała kwota, prognoza na koniec miesiąca oraz strefa "
        + "(Ok / Warning / Over), a także wydatki bez budżetu (w tym bez kategorii). Bez parametrów; "
        + "dotyczy bieżącego miesiąca. Kwoty w PLN, miesiąc w formacie RRRR-MM.";

    public override string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {}
        }
        """;

    private protected override async Task<object> RunAsync(JsonElement args, CofferDbContext db, CancellationToken ct)
    {
        var overview = await _budgetTracking.GetOverviewAsync(accountId: null, ct).ConfigureAwait(false);

        var budgets = overview.Budgets
            .Select(b => new
            {
                category = b.CategoryName,
                limit = b.Status.Limit,
                spent = b.Status.Spent,
                remaining = b.Status.Remaining,
                projected = b.Status.Projected,
                zone = b.Status.Zone.ToString(),
            })
            .ToList();

        var unbudgeted = overview.Unbudgeted
            .Select(u => new
            {
                category = u.CategoryName ?? UncategorizedLabel,
                spent = u.Spent,
            })
            .ToList();

        return new
        {
            month = overview.Month.ToString("yyyy-MM"),
            currency = DisplayCurrency,
            count = budgets.Count,
            budgets,
            unbudgeted,
        };
    }
}
