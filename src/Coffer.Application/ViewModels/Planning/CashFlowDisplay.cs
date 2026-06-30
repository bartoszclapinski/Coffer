using System.Globalization;
using Coffer.Core.Domain;

namespace Coffer.Application.ViewModels.Planning;

/// <summary>
/// Shared display helpers for the cash-flow planning page — resource keys for direction/interval
/// captions and language-independent money/date formatting (money stays pl-PL, the currency-format
/// rule). The localized lookups happen at the VM boundary via an injected <c>ILocalizer</c>; this
/// type only maps enums/values to keys and formats numbers and dates.
/// </summary>
internal static class CashFlowDisplay
{
    private static readonly CultureInfo _polish = CultureInfo.GetCultureInfo("pl-PL");

    public static string Money(decimal amount) => amount.ToString("N2", _polish) + " zł";

    /// <summary>Signed magnitude for a timeline row: outflows render negative, inflows positive.</summary>
    public static string SignedMoney(FlowDirection direction, decimal magnitude)
    {
        var signed = direction == FlowDirection.Outflow ? -magnitude : magnitude;
        return signed.ToString("N2", _polish) + " zł";
    }

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", _polish);

    public static string ShortDate(DateOnly date) => date.ToString("dd.MM", CultureInfo.InvariantCulture);

    /// <summary>The accrual period as a month caption, e.g. "luty 2026".</summary>
    public static string AccrualPeriod(DateOnly period) => period.ToString("LLLL yyyy", _polish);

    public static string DirectionKey(FlowDirection direction) => direction switch
    {
        FlowDirection.Inflow => "CashFlow.Direction.Inflow",
        FlowDirection.Outflow => "CashFlow.Direction.Outflow",
        _ => direction.ToString(),
    };

    public static string IntervalKey(int intervalMonths) => intervalMonths switch
    {
        1 => "CashFlow.Interval.Monthly",
        3 => "CashFlow.Interval.Quarterly",
        12 => "CashFlow.Interval.Yearly",
        _ => "CashFlow.Interval.Custom",
    };

    public static string DirectionColor(FlowDirection direction) =>
        direction == FlowDirection.Inflow ? "#34C759" : "#FF3B30";
}
