using System.Globalization;
using System.Text;
using Coffer.Core.Ai;
using Coffer.Core.Domain;
using Coffer.Core.Planning;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Planning;

/// <summary>
/// 16-C cash-flow narrator: turns the deterministic <see cref="CashFlowProjection"/> into a few
/// sentences of prose explaining the timing — when money leaves, when it arrives, and where the
/// running balance is tightest (doc 04 / Sprint-14 "the engine calculates, the AI explains"). One
/// provider call per run on <see cref="AiDefaults.ChatModel"/>, gated by <see cref="IAiBudgetGate"/>
/// at <see cref="AiPriority.Normal"/> and metered once as <see cref="AiPurpose.CashFlowExplain"/>. The
/// prompt is anonymised (hard rule #7) and the model is told to reuse only the figures it is given.
/// Any failure — over budget, offline, empty reply — yields a deterministic engine-only summary so the
/// planning page always has a narrative to show.
/// </summary>
public sealed class CashFlowExplainer : ICashFlowExplainer
{
    private const int CharsPerToken = 4;
    private const int OutputTokens = 320;
    private const int MaxPromptEvents = 40;

    private const string SystemPrompt =
        "You are a cash-flow planning assistant for a Polish user. You receive a deterministic forward "
        + "projection: an opening balance, a dated timeline of recurring inflows and outflows with the "
        + "running balance after each, and the lowest point the balance reaches. Your job: explain the "
        + "timing in 2-4 short Polish sentences — when money leaves, when it arrives, and where the "
        + "balance is tightest. Constraints: every number must come from the projection given — do NOT "
        + "invent or recompute any figure; if a tight point is flagged, name its date and amount; "
        + "politely decline any tax or investment recommendation; do not address the user by name. "
        + "Reply with plain prose only, no markdown, no lists.";

    private readonly IAiProvider _provider;
    private readonly IAiBudgetGate _budgetGate;
    private readonly IAiUsageLedger _ledger;
    private readonly IAiPricing _pricing;
    private readonly IPromptAnonymizer _anonymizer;
    private readonly ILogger<CashFlowExplainer> _logger;

    public CashFlowExplainer(
        IAiProvider provider,
        IAiBudgetGate budgetGate,
        IAiUsageLedger ledger,
        IAiPricing pricing,
        IPromptAnonymizer anonymizer,
        ILogger<CashFlowExplainer> logger)
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

    public async Task<CashFlowExplanation> ExplainAsync(CashFlowProjection projection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projection);

        if (projection.Events.Count == 0)
        {
            return EngineOnly(projection);
        }

        var prompt = BuildPrompt(projection);
        var model = AiDefaults.ChatModel;

        var estInputTokens = (SystemPrompt.Length + prompt.Length) / CharsPerToken;
        var estimate = _pricing.Estimate(model, estInputTokens, OutputTokens);
        if (!await _budgetGate.CanProceedAsync(estimate.Pln, AiPriority.Normal, ct).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "Budget gate blocked the cash-flow explanation for {Count} event(s); using engine-only summary.",
                projection.Events.Count);
            return EngineOnly(projection);
        }

        try
        {
            var request = new AiRequest
            {
                Prompt = prompt,
                Model = model,
                SystemPrompt = SystemPrompt,
                MaxTokens = OutputTokens + 128,
                Temperature = 0.4,
            };

            var response = await _provider.CompleteAsync(request, ct).ConfigureAwait(false);
            await _ledger.RecordAsync(response.Usage, AiPurpose.CashFlowExplain, ct).ConfigureAwait(false);

            var narrative = response.Value?.Trim();
            return string.IsNullOrEmpty(narrative)
                ? EngineOnly(projection)
                : new CashFlowExplanation(narrative, GeneratedByAi: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Cash-flow explanation failed for {Count} event(s); using engine-only summary.",
                projection.Events.Count);
            return EngineOnly(projection);
        }
    }

    private string BuildPrompt(CashFlowProjection projection)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"Window: {Iso(projection.From)} to {Iso(projection.To)}\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"Opening balance: {Money(projection.OpeningBalance)} PLN\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"Closing balance: {Money(projection.ClosingBalance)} PLN\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"Lowest balance: {Money(projection.LowestBalance)} PLN");
        if (projection.LowestBalanceDate is { } low)
        {
            sb.Append(CultureInfo.InvariantCulture, $" on {Iso(low)}");
        }

        sb.Append('\n');
        sb.Append(CultureInfo.InvariantCulture,
            $"Tight window: {(projection.HasTightWindow ? "yes" : "no")}\n");

        sb.Append("Events (date, direction, amount PLN, balanceAfter PLN, accrualPeriod, tight):\n");
        foreach (var e in projection.Events.Take(MaxPromptEvents))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"- {Iso(e.Date)} {e.Direction} {e.Name}: {Money(e.Amount)}, balance {Money(e.BalanceAfter)}, "
                + $"accrual {e.AccrualPeriod:yyyy-MM}, tight={(e.IsTight ? "yes" : "no")}\n");
        }

        return _anonymizer.Anonymize(sb.ToString());
    }

    private static CashFlowExplanation EngineOnly(CashFlowProjection projection)
    {
        var sb = new StringBuilder();
        if (projection.Events.Count == 0)
        {
            sb.Append("Brak zaplanowanych przepływów w wybranym horyzoncie. Saldo początkowe: ");
            sb.Append(PlnMoney(projection.OpeningBalance));
            sb.Append('.');
            return new CashFlowExplanation(sb.ToString(), GeneratedByAi: false);
        }

        sb.Append(CultureInfo.GetCultureInfo("pl-PL"),
            $"Saldo początkowe {PlnMoney(projection.OpeningBalance)}, prognozowane saldo końcowe "
            + $"{PlnMoney(projection.ClosingBalance)} na {PlDate(projection.To)}.");

        sb.Append(' ');
        if (projection.LowestBalanceDate is { } low)
        {
            sb.Append(CultureInfo.GetCultureInfo("pl-PL"),
                $"Najniższy punkt to {PlnMoney(projection.LowestBalance)} w dniu {PlDate(low)}.");
        }
        else
        {
            sb.Append(CultureInfo.GetCultureInfo("pl-PL"),
                $"Najniższy punkt to {PlnMoney(projection.LowestBalance)}.");
        }

        if (projection.HasTightWindow)
        {
            sb.Append(" Uwaga: saldo schodzi do lub poniżej zera — to napięte okno przed kolejnym wpływem.");
        }

        return new CashFlowExplanation(sb.ToString(), GeneratedByAi: false);
    }

    private static string Money(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string PlnMoney(decimal value) =>
        value.ToString("N2", CultureInfo.GetCultureInfo("pl-PL")) + " zł";

    private static string PlDate(DateOnly date) =>
        date.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("pl-PL"));

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
