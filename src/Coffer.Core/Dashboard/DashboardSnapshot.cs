using Coffer.Core.Transactions;

namespace Coffer.Core.Dashboard;

/// <summary>
/// Everything the dashboard renders, resolved in one pass: the current-month KPIs,
/// the top-categories breakdown, the daily and monthly spend trends, and the most
/// recent transactions. <see cref="HasData"/> is false for a fresh vault (no
/// transactions in the filtered scope) so the UI can show an empty state instead of
/// blank charts.
/// </summary>
public sealed record DashboardSnapshot(
    MonthlySummary CurrentMonth,
    IReadOnlyList<CategorySlice> TopCategories,
    IReadOnlyList<TrendPoint> DailySpend,
    IReadOnlyList<TrendPoint> MonthlySpend,
    IReadOnlyList<TransactionListItem> RecentTransactions,
    bool HasData);
