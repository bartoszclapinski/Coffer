using System.Globalization;
using System.Text.Json;
using Coffer.Core.Domain;
using Coffer.Core.Planning;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Chat;

/// <summary>
/// Read-only chat tool that answers "can I spend this amount?" (doc 07, Sprint 18-B). It resolves the
/// real per-account balance (18-A anchor), the active recurring flows, an estimated daily variable burn
/// and the owner's safety floor, then runs the deterministic <see cref="AffordabilityEngine"/> and
/// returns the verdict — afford/not, the projected low point, the headroom over the floor, and the
/// payment that pushes the owner under. Every number comes from the engine; the assistant only narrates
/// it (the Sprint-14 rule). Makes <b>zero</b> AI/provider calls. Amounts in PLN, dates RRRR-MM-DD.
/// </summary>
public sealed class CanIAffordTool : ChatTool
{
    private readonly IRunningBalanceQuery _balance;
    private readonly IBalanceTrustQuery _trust;
    private readonly IVariableBurnQuery _burn;
    private readonly IPlanningSettings _planningSettings;
    private readonly AffordabilityEngine _engine;

    public CanIAffordTool(
        IDbContextFactory<CofferDbContext> contextFactory,
        IRunningBalanceQuery balance,
        IBalanceTrustQuery trust,
        IVariableBurnQuery burn,
        IPlanningSettings planningSettings,
        AffordabilityEngine engine)
        : base(contextFactory)
    {
        ArgumentNullException.ThrowIfNull(balance);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(burn);
        ArgumentNullException.ThrowIfNull(planningSettings);
        ArgumentNullException.ThrowIfNull(engine);
        _balance = balance;
        _trust = trust;
        _burn = burn;
        _planningSettings = planningSettings;
        _engine = engine;
    }

    public override string Name => "CanIAfford";

    public override string Description =>
        "Odpowiada na pytanie „czy stać mnie na wydatek X?”. Na podstawie realnego salda konta, znanych "
        + "cyklicznych przepływów (raty, podatki, wypłata), szacowanego dziennego wydatku bieżącego oraz "
        + "osobistego progu bezpieczeństwa zwraca werdykt: czy stać (tak/nie), najniższy punkt salda do "
        + "najbliższego wpływu, zapas ponad progiem oraz płatność, która najbardziej obciąża budżet. "
        + "Wynik bywa oznaczony jako niepewny (luka w wyciągach) lub względny (brak zakotwiczonego salda). "
        + "Parametry: amount (wymagany), date (opcjonalny, domyślnie dziś), accountId (opcjonalny). Kwoty "
        + "w PLN, daty w formacie RRRR-MM-DD.";

    public override string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "amount": {
              "type": "number",
              "description": "Kwota wydatku do sprawdzenia (w PLN, dodatnia)."
            },
            "date": {
              "type": "string",
              "description": "Data wydatku w formacie RRRR-MM-DD (domyślnie dziś)."
            },
            "accountId": {
              "type": "string",
              "description": "Identyfikator konta (GUID); pominięty = saldo łączne wszystkich kont (względne)."
            }
          },
          "required": ["amount"]
        }
        """;

    private protected override async Task<object> RunAsync(JsonElement args, CofferDbContext db, CancellationToken ct)
    {
        if (!TryGetDecimal(args, "amount", out var amount) || amount <= 0m)
        {
            return ErrorObject("Parameter 'amount' is required and must be a positive number.");
        }

        var spendDate = TryGetDate(args, "date", out var date) ? date : DateOnly.FromDateTime(DateTime.Now);

        Guid? accountId = null;
        var accountIdRaw = GetString(args, "accountId");
        if (!string.IsNullOrWhiteSpace(accountIdRaw))
        {
            if (!Guid.TryParse(accountIdRaw, out var parsed))
            {
                return ErrorObject("Parameter 'accountId' was not a valid GUID.");
            }

            accountId = parsed;
        }

        var flows = await db.RecurringFlows.AsNoTracking()
            .Where(f => f.IsActive && f.Currency == DisplayCurrency)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var opening = await _balance.GetBalanceAsOfAsync(spendDate, accountId, ct).ConfigureAwait(false);
        var dailyBurn = await _burn.GetDailyBurnAsync(accountId, spendDate, ct).ConfigureAwait(false);
        var safetyFloor = await _planningSettings.GetSafetyFloorPlnAsync(ct).ConfigureAwait(false);

        bool isRelative;
        BalanceTrust trust;
        if (accountId is Guid id)
        {
            var anchorDate = await db.Accounts.AsNoTracking()
                .Where(a => a.Id == id)
                .Select(a => a.AnchorDate)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            isRelative = anchorDate is null;
            trust = await _trust.CheckAsync(id, spendDate, ct).ConfigureAwait(false);
        }
        else
        {
            // A cross-account blend has no single anchor and no per-account continuity signal; report it
            // as relative and do not fabricate an uncertainty flag.
            isRelative = true;
            trust = new BalanceTrust(IsTrustworthy: true, WindowFrom: spendDate, Gaps: Array.Empty<StatementGap>());
        }

        var verdict = _engine.Assess(amount, spendDate, opening, flows, dailyBurn, safetyFloor, trust, isRelative);

        return new
        {
            canAfford = verdict.CanAfford,
            amount = verdict.SpendAmount,
            date = Iso(verdict.SpendDate),
            accountId = accountId?.ToString(),
            currency = DisplayCurrency,
            openingBalance = verdict.OpeningBalance,
            lowestBalance = verdict.LowestBalance,
            lowestBalanceDate = Iso(verdict.LowestBalanceDate),
            safetyFloor = verdict.SafetyFloor,
            headroom = verdict.Headroom,
            dailyBurn = verdict.DailyBurn,
            nextInflowDate = verdict.NextInflowDate is { } inflow ? Iso(inflow) : null,
            driver = verdict.Driver is { } d
                ? new { name = d.Name, date = Iso(d.Date), amount = d.Amount }
                : null,
            isUncertain = verdict.IsUncertain,
            uncertaintyGap = verdict.UncertaintyGap is { } gap
                ? new { from = Iso(gap.From), to = Iso(gap.To) }
                : null,
            isRelative = verdict.IsRelative,
        };
    }

    private static bool TryGetDecimal(JsonElement args, string name, out decimal value)
    {
        value = default;
        return args.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetDecimal(out value);
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
