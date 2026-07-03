using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Forecast;
using Coffer.Core.Forecasting;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Forecast;

public class ForecastViewModelTests
{
    private static readonly Guid _groceriesId = Guid.NewGuid();

    [Fact]
    public async Task Load_PopulatesRowsAndTotal()
    {
        var query = new FakeExpenseForecastQuery
        {
            Forecast = new ExpenseForecast(
                new DateOnly(2026, 8, 1),
                [
                    new CategoryForecast(_groceriesId, "Groceries", "#0F0", 40m, 210m, 250m, 250m, 1000m),
                    new CategoryForecast(null, null, null, 0m, 90m, 90m, 90m, null),
                ],
                340m),
        };
        var vm = Create(query, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.MonthText.Should().NotBeNullOrEmpty();
        vm.TotalText.Should().NotBeNullOrEmpty();
        vm.HasForecast.Should().BeTrue();
        vm.IsEmpty.Should().BeFalse();
        vm.Forecasts.Should().HaveCount(2);

        var groceries = vm.Forecasts.First();
        groceries.CategoryName.Should().Be("Groceries");
        groceries.CanAccept.Should().BeTrue();
        groceries.SuggestedLimit.Should().Be(250m);

        var uncategorised = vm.Forecasts.Last();
        uncategorised.CategoryName.Should().Be("Forecast.Uncategorized"); // FakeLocalizer echoes the key
        uncategorised.CanAccept.Should().BeFalse("the uncategorised bucket has no category to budget");
    }

    [Fact]
    public async Task Load_EmptyForecast_SetsIsEmpty()
    {
        var vm = Create(new FakeExpenseForecastQuery(), out _);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasForecast.Should().BeFalse();
        vm.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task AcceptSuggestion_SetsBudgetAndReloads()
    {
        var query = new FakeExpenseForecastQuery
        {
            Forecast = new ExpenseForecast(
                new DateOnly(2026, 8, 1),
                [new CategoryForecast(_groceriesId, "Groceries", "#0F0", 40m, 210m, 250m, 250m, null)],
                250m),
        };
        var vm = Create(query, out var repo);
        await vm.LoadCommand.ExecuteAsync(null);
        var callsAfterLoad = query.Calls;

        await vm.AcceptSuggestionCommand.ExecuteAsync(vm.Forecasts.First());

        repo.SetCalls.Should().Be(1);
        repo.LastSet.Should().Be((_groceriesId, 250m, "PLN"));
        query.Calls.Should().Be(callsAfterLoad + 1, "the forecast reloads after setting the budget");
    }

    [Fact]
    public async Task AcceptSuggestion_UncategorisedRow_DoesNothing()
    {
        var query = new FakeExpenseForecastQuery
        {
            Forecast = new ExpenseForecast(
                new DateOnly(2026, 8, 1),
                [new CategoryForecast(null, null, null, 0m, 90m, 90m, 90m, null)],
                90m),
        };
        var vm = Create(query, out var repo);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.AcceptSuggestionCommand.ExecuteAsync(vm.Forecasts.Single());

        repo.SetCalls.Should().Be(0, "there is no category to set a budget for");
    }

    private static ForecastViewModel Create(FakeExpenseForecastQuery query, out FakeCategoryBudgetRepository repo)
    {
        repo = new FakeCategoryBudgetRepository();
        return new ForecastViewModel(
            query,
            repo,
            new FakeLocalizer(),
            NullLogger<ForecastViewModel>.Instance);
    }
}
