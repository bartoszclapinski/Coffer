using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Dashboard;
using Coffer.Core.Dashboard;
using Coffer.Core.Transactions;
using FluentAssertions;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Dashboard;

public class DashboardViewModelTests
{
    [Fact]
    public async Task Load_PopulatesKpisAndCollectionsFromSnapshot()
    {
        var snapshot = new DashboardSnapshot(
            new MonthlySummary(new DateOnly(2026, 6, 1), Spend: 320.50m, Income: 5000m, Net: 4679.50m, "PLN", 3),
            TopCategories:
            [
                new CategorySlice(Guid.NewGuid(), "Paliwo", "#FF9500", 300m, 0.6),
                new CategorySlice(null, "Bez kategorii", "#8E8E93", 200m, 0.4),
            ],
            DailySpend: [new TrendPoint(new DateOnly(2026, 6, 14), 40m), new TrendPoint(new DateOnly(2026, 6, 15), 60m)],
            MonthlySpend: [new TrendPoint(new DateOnly(2026, 5, 1), 100m), new TrendPoint(new DateOnly(2026, 6, 1), 200m)],
            RecentTransactions:
            [
                new TransactionListItem(
                    Guid.NewGuid(), new DateOnly(2026, 6, 15), "tx", null, -60m, "PLN",
                    Guid.NewGuid(), "PKO", null, "Paliwo", "#FF9500"),
            ],
            HasData: true);
        var vm = new DashboardViewModel(new FakeDashboardQuery(snapshot), new FakeLocalizer(), NullLogger<DashboardViewModel>.Instance);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasData.Should().BeTrue();
        vm.IsEmpty.Should().BeFalse();
        vm.Spend.Should().Be(320.50m);
        vm.Income.Should().Be(5000m);
        vm.Net.Should().Be(4679.50m);
        vm.Currency.Should().Be("PLN");
        vm.TransactionCount.Should().Be(3);
        vm.TopCategories.Should().HaveCount(2);
        vm.RecentTransactions.Should().ContainSingle();
        vm.CategorySeries.Should().HaveCount(2);
        vm.DailySpendSeries.Should().ContainSingle().Which.Should().BeOfType<LineSeries<decimal>>();
        vm.MonthlySpendSeries.Should().ContainSingle().Which.Should().BeOfType<ColumnSeries<decimal>>();
    }

    [Fact]
    public async Task Load_DefaultFilterUsesPlnAndNoAccount()
    {
        var query = new FakeDashboardQuery();
        var vm = new DashboardViewModel(query, new FakeLocalizer(), NullLogger<DashboardViewModel>.Instance);

        await vm.LoadCommand.ExecuteAsync(null);

        query.Calls.Should().Be(1);
        query.LastFilter!.Currency.Should().Be("PLN");
        query.LastFilter.AccountId.Should().BeNull();
    }

    [Fact]
    public async Task Load_EmptySnapshot_ReportsEmptyState()
    {
        var vm = new DashboardViewModel(new FakeDashboardQuery(), new FakeLocalizer(), NullLogger<DashboardViewModel>.Instance);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasData.Should().BeFalse();
        vm.IsEmpty.Should().BeTrue();
        vm.RecentTransactions.Should().BeEmpty();
        vm.CategorySeries.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_WhenQueryThrows_SetsErrorAndClearsEmptyState()
    {
        var query = new FakeDashboardQuery { Throw = new InvalidOperationException("boom") };
        var vm = new DashboardViewModel(query, new FakeLocalizer(), NullLogger<DashboardViewModel>.Instance);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().NotBeNullOrEmpty();
        vm.IsLoading.Should().BeFalse();
        vm.IsEmpty.Should().BeFalse("an error is its own state, not the empty state");
    }
}
