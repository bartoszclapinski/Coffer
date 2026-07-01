using System.Globalization;
using System.Text.Json;
using Coffer.Core.Planning;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Chat;

/// <summary>
/// Read-only chat tool that returns the deterministic forward cash-flow projection (doc 04, 16-C) so
/// the assistant can answer timing questions — "when does the next big payment leave?", "where am I
/// tight before salary?". It runs the same <see cref="CashFlowProjectionEngine"/> the planning page
/// uses: active <see cref="Core.Domain.RecurringFlow"/>s, the opening balance from
/// <see cref="IRunningBalanceQuery"/>, projected over a caller-chosen horizon. Numbers come from the
/// engine — the assistant only narrates them (the Sprint-14 rule). Amounts in PLN, dates RRRR-MM-DD.
/// </summary>
public sealed class GetCashFlowProjectionTool : ChatTool
{
    private const int DefaultHorizonDays = 60;
    private const int MaxHorizonDays = 365;

    private readonly IRecurringFlowRepository _flows;
    private readonly IRunningBalanceQuery _balance;
    private readonly IPlanningSettings _planningSettings;
    private readonly CashFlowProjectionEngine _engine;

    public GetCashFlowProjectionTool(
        IDbContextFactory<CofferDbContext> contextFactory,
        IRecurringFlowRepository flows,
        IRunningBalanceQuery balance,
        IPlanningSettings planningSettings,
        CashFlowProjectionEngine engine)
        : base(contextFactory)
    {
        ArgumentNullException.ThrowIfNull(flows);
        ArgumentNullException.ThrowIfNull(balance);
        ArgumentNullException.ThrowIfNull(planningSettings);
        ArgumentNullException.ThrowIfNull(engine);
        _flows = flows;
        _balance = balance;
        _planningSettings = planningSettings;
        _engine = engine;
    }

    public override string Name => "GetCashFlowProjection";

    public override string Description =>
        "Prognoza przepływów gotówki do przodu: saldo początkowe i końcowe, najniższy punkt salda (z "
        + "datą), ostrzeżenie o napiętym oknie oraz datowana lista nadchodzących wpływów i wydatków z "
        + "saldem po każdym. Opcjonalny parametr horizonDays (domyślnie 60, maksymalnie 365). Kwoty w "
        + "PLN, daty w formacie RRRR-MM-DD.";

    public override string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "horizonDays": {
              "type": "integer",
              "description": "Liczba dni w przód do prognozy (1-365, domyślnie 60)."
            }
          }
        }
        """;

    private protected override async Task<object> RunAsync(JsonElement args, CofferDbContext db, CancellationToken ct)
    {
        var horizonDays = Math.Clamp(GetInt(args, "horizonDays", DefaultHorizonDays), 1, MaxHorizonDays);

        var flows = await _flows.GetActiveAsync(ct).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var opening = await _balance.GetBalanceAsOfAsync(today, accountId: null, ct).ConfigureAwait(false);
        var safetyFloor = await _planningSettings.GetSafetyFloorPlnAsync(ct).ConfigureAwait(false);

        if (flows.Count == 0)
        {
            return new
            {
                horizonDays,
                openingBalance = opening,
                currency = DisplayCurrency,
                eventCount = 0,
                events = Array.Empty<object>(),
            };
        }

        var projection = _engine.Project(flows, opening, today, horizonDays, safetyFloor);

        var events = projection.Events
            .Select(e => new
            {
                date = Iso(e.Date),
                name = e.Name,
                direction = e.Direction.ToString(),
                amount = e.Amount,
                balanceAfter = e.BalanceAfter,
                accrualPeriod = e.AccrualPeriod.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                isTight = e.IsTight,
            })
            .ToList();

        return new
        {
            from = Iso(projection.From),
            to = Iso(projection.To),
            horizonDays,
            currency = DisplayCurrency,
            openingBalance = projection.OpeningBalance,
            closingBalance = projection.ClosingBalance,
            lowestBalance = projection.LowestBalance,
            lowestBalanceDate = projection.LowestBalanceDate is { } d ? Iso(d) : null,
            hasTightWindow = projection.HasTightWindow,
            eventCount = events.Count,
            events,
        };
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
