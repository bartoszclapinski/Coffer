using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Budgets;
using Coffer.Core.Budgeting;
using Coffer.Core.Categorization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Budgets;

public class BudgetsViewModelTests
{
    private static readonly Guid _groceriesId = Guid.NewGuid();

    [Fact]
    public async Task Load_PopulatesCategoriesBudgetsAndUnbudgeted()
    {
        var query = new FakeBudgetTrackingQuery
        {
            Overview = new BudgetOverview(
                new DateOnly(2026, 3, 1),
                [new BudgetLine(_groceriesId, "Groceries", "#0F0",
                    new BudgetStatus(1000m, 800m, 200m, 0.8m, 1653m, BudgetZone.Warning))],
                [new UnbudgetedLine(null, null, null, 50m)]),
        };
        var vm = Create(query, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Categories.Should().ContainSingle();
        vm.MonthText.Should().NotBeNullOrEmpty();
        vm.HasBudgets.Should().BeTrue();
        vm.HasUnbudgeted.Should().BeTrue();
        vm.IsEmpty.Should().BeFalse();

        var row = vm.Budgets.Single();
        row.CategoryName.Should().Be("Groceries");
        row.ZoneLabel.Should().Be("Budgets.Zone.Warning"); // FakeLocalizer echoes the key
        row.BarValue.Should().BeApproximately(80d, 0.01);
        vm.Unbudgeted.Single().Label.Should().Be("Budgets.Uncategorized");
    }

    [Fact]
    public async Task Load_OverBudget_ClampsBarToOneHundred()
    {
        var query = new FakeBudgetTrackingQuery
        {
            Overview = new BudgetOverview(
                new DateOnly(2026, 3, 1),
                [new BudgetLine(_groceriesId, "Groceries", "#0F0",
                    new BudgetStatus(1000m, 1200m, -200m, 1.2m, 1500m, BudgetZone.Over))],
                []),
        };
        var vm = Create(query, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        var row = vm.Budgets.Single();
        row.BarValue.Should().Be(100d);
        row.ZoneLabel.Should().Be("Budgets.Zone.Over");
        row.PercentText.Should().Be("120%");
    }

    [Fact]
    public async Task AddBudget_WithCategoryAndLimit_PersistsAndReloads()
    {
        var query = new FakeBudgetTrackingQuery();
        var vm = Create(query, out var repo);
        await vm.LoadCommand.ExecuteAsync(null);
        var callsAfterLoad = query.Calls;

        vm.SelectedCategory = vm.Categories.First();
        vm.NewLimit = 1500m;
        await vm.AddBudgetCommand.ExecuteAsync(null);

        repo.SetCalls.Should().Be(1);
        repo.LastSet.Should().Be((_groceriesId, 1500m, "PLN"));
        vm.NewLimit.Should().Be(0m, "the form resets after a successful save");
        query.Calls.Should().Be(callsAfterLoad + 1, "the overview reloads after saving");
    }

    [Fact]
    public async Task AddBudget_WithoutCategory_ShowsErrorAndDoesNotPersist()
    {
        var vm = Create(new FakeBudgetTrackingQuery(), out var repo);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedCategory = null;
        vm.NewLimit = 500m;
        await vm.AddBudgetCommand.ExecuteAsync(null);

        repo.SetCalls.Should().Be(0);
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AddBudget_WithZeroLimit_ShowsErrorAndDoesNotPersist()
    {
        var vm = Create(new FakeBudgetTrackingQuery(), out var repo);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedCategory = vm.Categories.First();
        vm.NewLimit = 0m;
        await vm.AddBudgetCommand.ExecuteAsync(null);

        repo.SetCalls.Should().Be(0);
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RemoveBudget_DeletesAndReloads()
    {
        var query = new FakeBudgetTrackingQuery
        {
            Overview = new BudgetOverview(
                new DateOnly(2026, 3, 1),
                [new BudgetLine(_groceriesId, "Groceries", "#0F0",
                    new BudgetStatus(1000m, 100m, 900m, 0.1m, 300m, BudgetZone.Ok))],
                []),
        };
        var vm = Create(query, out var repo);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.RemoveBudgetCommand.ExecuteAsync(vm.Budgets.Single());

        repo.RemoveCalls.Should().Be(1);
        repo.LastRemoved.Should().Be(_groceriesId);
    }

    private static BudgetsViewModel Create(FakeBudgetTrackingQuery query, out FakeCategoryBudgetRepository repo)
    {
        repo = new FakeCategoryBudgetRepository();
        var categories = new FakeCategoryService(new CategoryListItem(_groceriesId, "Groceries", "#0F0"));
        return new BudgetsViewModel(
            repo,
            query,
            categories,
            new FakeLocalizer(),
            NullLogger<BudgetsViewModel>.Instance);
    }
}
