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

    private static GoalsViewModel CreateViewModel(
        List<Goal> store,
        out FakeGoalService service,
        out FakeGoalFeasibilityEngine engine)
    {
        var query = new FakeGoalsQuery();
        query.Goals.AddRange(store);
        service = new FakeGoalService(query.Goals);
        engine = new FakeGoalFeasibilityEngine();

        return new GoalsViewModel(
            query,
            service,
            new FakeFinancialContextBuilder(),
            engine,
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
