using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Goals;
using Coffer.Core.Domain;
using Coffer.Core.Goals;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Goals;

public class GoalsViewModelTests
{
    [Fact]
    public async Task LoadAsync_PopulatesGoalsAndSelectsFirst()
    {
        var store = new List<Goal> { Goal("Wakacje", 8000m), Goal("Laptop", 5000m) };
        var vm = CreateViewModel(store, out _, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Goals.Should().HaveCount(2);
        vm.HasGoals.Should().BeTrue();
        vm.IsEmpty.Should().BeFalse();
        vm.SelectedGoal.Should().NotBeNull();
        vm.SelectedGoal!.Id.Should().Be(store[0].Id);
    }

    [Fact]
    public async Task LoadAsync_NoGoals_IsEmpty()
    {
        var vm = CreateViewModel([], out _, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasGoals.Should().BeFalse();
        vm.IsEmpty.Should().BeTrue();
        vm.SelectedGoal.Should().BeNull();
    }

    [Fact]
    public async Task CreateGoal_WithValidInput_CreatesAndReloads()
    {
        var vm = CreateViewModel([], out var service, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.NewGoalName = "Nowy cel";
        vm.NewGoalTargetAmount = 3000m;
        vm.NewGoalTargetDate = new DateTimeOffset(new DateTime(2027, 7, 1));

        await vm.CreateGoalCommand.ExecuteAsync(null);

        service.Created.Should().ContainSingle();
        service.Created[0].Name.Should().Be("Nowy cel");
        service.Created[0].Currency.Should().Be("PLN");
        vm.Goals.Should().ContainSingle();
        vm.NewGoalName.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateGoal_WithInvalidInput_SetsErrorAndDoesNotCreate()
    {
        var vm = CreateViewModel([], out var service, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.NewGoalName = "   ";
        vm.NewGoalTargetAmount = 0m;

        await vm.CreateGoalCommand.ExecuteAsync(null);

        service.Created.Should().BeEmpty();
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Archive_RemovesGoalFromActiveList()
    {
        var store = new List<Goal> { Goal("Wakacje", 8000m), Goal("Laptop", 5000m) };
        var vm = CreateViewModel(store, out var service, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        var first = vm.SelectedGoal!;
        await first.ArchiveCommand.ExecuteAsync(null);

        service.Archived.Should().Contain(first.Id);
        vm.Goals.Should().ContainSingle();
        vm.Goals.Should().NotContain(g => g.Id == first.Id);
    }

    [Fact]
    public async Task AddContribution_RecordsContributionAndReloads()
    {
        var store = new List<Goal> { Goal("Wakacje", 8000m) };
        var vm = CreateViewModel(store, out var service, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        var goal = vm.SelectedGoal!;
        goal.ContributionAmount = 500m;
        await goal.AddContributionCommand.ExecuteAsync(null);

        service.Contributions.Should().ContainSingle();
        service.Contributions[0].Amount.Should().Be(500m);
        service.Contributions[0].GoalId.Should().Be(goal.Id);
    }

    [Fact]
    public async Task LoadAsync_SurfacesAdvisorSuggestions_FromLatestReport()
    {
        var store = new List<Goal> { Goal("Wakacje", 8000m) };
        var vm = CreateViewModel(store, out _, out _, out var reportQuery);
        reportQuery.Report = new AdvisorReport
        {
            Id = Guid.NewGuid(),
            Date = new DateOnly(2026, 6, 25),
            GeneratedAt = DateTime.UtcNow,
            GeneratedByAi = true,
            Entries =
            [
                new AdvisorSuggestion
                {
                    Id = Guid.NewGuid(),
                    Kind = AdvisorEntryKind.Suggestion,
                    Title = "Restauracje do średniej",
                    Savings = 329m,
                    Description = "Z 540 do 211 zł.",
                    CategoryAffected = "Restauracje",
                },
                new AdvisorSuggestion
                {
                    Id = Guid.NewGuid(),
                    Kind = AdvisorEntryKind.Risk,
                    GoalId = store[0].Id,
                    Description = "Napięty termin.",
                },
            ],
        };

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasSuggestions.Should().BeTrue();
        vm.SuggestionsAreEngineOnly.Should().BeFalse();
        vm.Suggestions.Should().ContainSingle("only Suggestion entries are surfaced, not Risk entries");
        vm.Suggestions[0].Title.Should().Be("Restauracje do średniej");
        vm.Suggestions[0].CategoryText.Should().Be("Restauracje");
        vm.Suggestions[0].SavingsText.Should().Contain("329");
    }

    [Fact]
    public async Task LoadAsync_NoReport_HasNoSuggestions()
    {
        var vm = CreateViewModel([Goal("Wakacje", 8000m)], out _, out _, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasSuggestions.Should().BeFalse();
        vm.Suggestions.Should().BeEmpty();
    }

    private static GoalsViewModel CreateViewModel(
        List<Goal> store,
        out FakeGoalService service,
        out FakeGoalFeasibilityEngine engine) =>
        CreateViewModel(store, out service, out engine, out _);

    private static GoalsViewModel CreateViewModel(
        List<Goal> store,
        out FakeGoalService service,
        out FakeGoalFeasibilityEngine engine,
        out FakeAdvisorReportQuery reportQuery)
    {
        var query = new FakeGoalsQuery();
        query.Goals.AddRange(store);
        service = new FakeGoalService(query.Goals);
        engine = new FakeGoalFeasibilityEngine();
        reportQuery = new FakeAdvisorReportQuery();

        return new GoalsViewModel(
            query,
            service,
            new FakeFinancialContextBuilder(),
            engine,
            reportQuery,
            NullLogger<GoalsViewModel>.Instance);
    }

    private static Goal Goal(string name, decimal target) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = GoalType.Purchase,
        TargetAmount = target,
        Currency = "PLN",
        TargetDate = new DateOnly(2027, 7, 1),
        Priority = Priority.Medium,
        CreatedAt = DateTime.UtcNow,
    };
}
