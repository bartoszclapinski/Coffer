using Coffer.Core.Dashboard;
using Coffer.Core.Transactions;

namespace Coffer.Desktop.Preview;

/// <summary>
/// Dev-only <see cref="IDashboardQuery"/> returning canned PLN sample data so the Overview
/// screen can be rendered and reviewed (in both themes) without a database or a login. Wired
/// only behind the <c>COFFER_OVERVIEW_PREVIEW</c> environment variable — never in normal use.
/// </summary>
internal sealed class PreviewDashboardQuery : IDashboardQuery
{
    public Task<DashboardSnapshot> GetSnapshotAsync(DashboardFilter filter, CancellationToken ct)
    {
        var month = new DateOnly(2026, 7, 1);

        var categories = new[]
        {
            new CategorySlice(Guid.NewGuid(), "Groceries", "#1C6E6A", 1320m, 0.32),
            new CategorySlice(Guid.NewGuid(), "Dining", "#A8552F", 880m, 0.21),
            new CategorySlice(Guid.NewGuid(), "Transport", "#3D5AA6", 640m, 0.15),
            new CategorySlice(Guid.NewGuid(), "Subscriptions", "#7A4A7E", 420m, 0.10),
            new CategorySlice(Guid.NewGuid(), "Housing", "#6B655C", 920m, 0.22),
        };

        var dailyStart = new DateOnly(2026, 6, 8);
        var daily = new List<TrendPoint>(30);
        for (var i = 0; i < 30; i++)
        {
            // A gentle wave so the area chart has shape without random noise.
            var wave = 120m + (i * 6m) + (decimal)(Math.Sin(i / 3.0) * 55.0);
            daily.Add(new TrendPoint(dailyStart.AddDays(i), Math.Round(wave, 2)));
        }

        var monthly = new List<TrendPoint>(12);
        for (var i = 0; i < 12; i++)
        {
            var v = 3600m + (decimal)(Math.Sin(i / 2.0) * 700.0) + (i * 40m);
            monthly.Add(new TrendPoint(month.AddMonths(i - 11), Math.Round(v, 2)));
        }

        var recent = new[]
        {
            Tx("Biedronka", "Groceries", "#1C6E6A", -86.40m, 8),
            Tx("Uber", "Transport", "#3D5AA6", -18.30m, 8),
            Tx("Sweetgreen", "Dining", "#A8552F", -16.80m, 5),
            Tx("Spotify", "Subscriptions", "#7A4A7E", -23.99m, 3),
            Tx("Netflix", "Subscriptions", "#7A4A7E", -43.00m, 2),
            Tx("Wynagrodzenie", "Income", "#2F6B4F", 8900.00m, 1),
        };

        var snapshot = new DashboardSnapshot(
            new MonthlySummary(month, Spend: 4180.00m, Income: 8900.00m, Net: 4720.00m, Currency: "PLN", TransactionCount: 42),
            categories,
            daily,
            monthly,
            recent,
            HasData: true);

        return Task.FromResult(snapshot);

        static TransactionListItem Tx(string desc, string cat, string color, decimal amount, int day) =>
            new(Guid.NewGuid(), new DateOnly(2026, 7, day), desc, desc, amount, "PLN",
                Guid.NewGuid(), "Konto", Guid.NewGuid(), cat, color);
    }
}
