namespace Coffer.Core.Dashboard;

/// <summary>
/// KPI roll-up for a single month. <see cref="Spend"/> is the positive magnitude of
/// debits (negative amounts), <see cref="Income"/> the sum of credits, and
/// <see cref="Net"/> their difference (income − spend, i.e. the signed sum). All
/// monetary fields are <c>decimal</c> in <see cref="Currency"/> (hard rules #1/#9);
/// <see cref="Month"/> is the first day of the month covered.
/// </summary>
public sealed record MonthlySummary(
    DateOnly Month,
    decimal Spend,
    decimal Income,
    decimal Net,
    string Currency,
    int TransactionCount)
{
    public static MonthlySummary Empty(DateOnly month, string currency) =>
        new(month, 0m, 0m, 0m, currency, 0);
}
