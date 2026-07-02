using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Planning;
using Coffer.Core.Domain;
using Coffer.Core.Planning;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Planning;

public class CashFlowPlanningViewModelTests
{
    private static readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task LoadAsync_ProjectsFlowsAndPopulatesTimeline()
    {
        var repo = new FakeRecurringFlowRepository(MonthlyOutflow("Rata", 500m, day: 10));
        var vm = CreateViewModel(repo, out _, out var balance, out _);
        balance.Balance = 3000m;

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasData.Should().BeTrue();
        vm.IsEmpty.Should().BeFalse();
        vm.Flows.Should().ContainSingle();
        vm.Timeline.Should().NotBeEmpty();
        vm.OpeningBalanceText.Should().NotBeNullOrEmpty();
        vm.ClosingBalanceText.Should().NotBeNullOrEmpty();
        vm.BalanceSeries.Should().ContainSingle();
        vm.BalanceXAxes.Should().ContainSingle();
    }

    [Fact]
    public async Task LoadAsync_NoFlows_IsEmpty()
    {
        var vm = CreateViewModel(new FakeRecurringFlowRepository(), out _, out _, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasData.Should().BeFalse();
        vm.IsEmpty.Should().BeTrue();
        vm.Timeline.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangingHorizon_ReprojectsWithoutRequerying()
    {
        var repo = new FakeRecurringFlowRepository(MonthlyOutflow("Rata", 500m, day: 10));
        var vm = CreateViewModel(repo, out var detector, out _, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        var detectCallsAfterLoad = detector.Calls;
        var shortHorizon = vm.Timeline.Count;

        vm.SelectedHorizon = vm.HorizonOptions[3]; // 365 days

        vm.Timeline.Count.Should().BeGreaterThan(shortHorizon);
        detector.Calls.Should().Be(detectCallsAfterLoad, "horizon changes re-project from cached state without re-querying");
    }

    [Fact]
    public async Task AddFlow_WithValidInput_PersistsAndReloads()
    {
        var repo = new FakeRecurringFlowRepository();
        var vm = CreateViewModel(repo, out _, out _, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.NewFlowName = "Pensja";
        vm.NewFlowAmount = 9000m;
        vm.NewFlowAnchorDay = 28;
        vm.NewFlowDirection = vm.DirectionOptions.First(d => d.Value == FlowDirection.Inflow);

        await vm.AddFlowCommand.ExecuteAsync(null);

        repo.Store.Should().ContainSingle();
        repo.Store[0].Name.Should().Be("Pensja");
        repo.Store[0].Currency.Should().Be("PLN");
        repo.Store[0].Source.Should().Be(FlowSource.Manual);
        vm.Flows.Should().ContainSingle();
        vm.NewFlowName.Should().BeEmpty();
    }

    [Fact]
    public async Task AddFlow_WithInvalidInput_SetsErrorAndDoesNotPersist()
    {
        var repo = new FakeRecurringFlowRepository();
        var vm = CreateViewModel(repo, out _, out _, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.NewFlowName = "   ";
        vm.NewFlowAmount = 0m;

        await vm.AddFlowCommand.ExecuteAsync(null);

        repo.Store.Should().BeEmpty();
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DeleteFlow_RemovesAndReloads()
    {
        var repo = new FakeRecurringFlowRepository(MonthlyOutflow("Rata", 500m, day: 10));
        var vm = CreateViewModel(repo, out _, out _, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.Flows[0].DeleteCommand.ExecuteAsync(null);

        repo.Store.Should().BeEmpty();
        vm.Flows.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveFlow_UpdatesUnderlyingFlow()
    {
        var flow = MonthlyOutflow("Rata", 500m, day: 10);
        var repo = new FakeRecurringFlowRepository(flow);
        var vm = CreateViewModel(repo, out _, out _, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        var row = vm.Flows[0];
        row.Amount = 750m;
        row.Name = "Rata auto";
        await row.SaveCommand.ExecuteAsync(null);

        repo.Store.Should().ContainSingle();
        repo.Store[0].TypicalAmount.Should().Be(750m);
        repo.Store[0].Name.Should().Be("Rata auto");
        repo.Store[0].Id.Should().Be(flow.Id);
    }

    [Fact]
    public async Task Suggestions_ExcludeCandidatesAlreadyConfirmedByMerchant()
    {
        var existing = MonthlyOutflow("Netflix", 43m, day: 5);
        existing.MatchMerchant = "NETFLIX";
        var repo = new FakeRecurringFlowRepository(existing);
        var vm = CreateViewModel(repo, out var detector, out _, out _);
        detector.Candidates.Add(Candidate("Netflix", "NETFLIX"));
        detector.Candidates.Add(Candidate("Spotify", "SPOTIFY"));

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasSuggestions.Should().BeTrue();
        vm.Suggestions.Should().ContainSingle();
        vm.Suggestions[0].Name.Should().Be("Spotify");
    }

    [Fact]
    public async Task ConfirmCandidate_PersistsAsDetectedFlow()
    {
        var repo = new FakeRecurringFlowRepository();
        var vm = CreateViewModel(repo, out var detector, out _, out _);
        detector.Candidates.Add(Candidate("Spotify", "SPOTIFY"));
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.Suggestions[0].ConfirmCommand.ExecuteAsync(null);

        repo.Store.Should().ContainSingle();
        repo.Store[0].Name.Should().Be("Spotify");
        repo.Store[0].MatchMerchant.Should().Be("SPOTIFY");
        repo.Store[0].Source.Should().Be(FlowSource.Detected);
        repo.Store[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ContinuityGaps_SurfaceWarning()
    {
        var repo = new FakeRecurringFlowRepository(MonthlyOutflow("Rata", 500m, day: 10));
        var vm = CreateViewModel(repo, out _, out _, out var continuity);
        continuity.Gaps.Add(new StatementGap(Guid.NewGuid(), new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasGaps.Should().BeTrue();
        vm.StatementGaps.Should().ContainSingle();
    }

    [Fact]
    public async Task TightBalance_FlagsTightWindow()
    {
        var repo = new FakeRecurringFlowRepository(MonthlyOutflow("Rata", 5000m, day: 10));
        var vm = CreateViewModel(repo, out _, out var balance, out _);
        balance.Balance = 100m;

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasTightWindow.Should().BeTrue();
    }

    [Fact]
    public async Task Explain_SurfacesNarrativeForCurrentProjection()
    {
        var repo = new FakeRecurringFlowRepository(MonthlyOutflow("Rata", 500m, day: 10));
        var explainer = new FakeCashFlowExplainer
        {
            Result = new CashFlowExplanation("Rata wychodzi 10-tego.", GeneratedByAi: true),
        };
        var vm = CreateViewModel(repo, explainer);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.ExplainCommand.ExecuteAsync(null);

        vm.HasNarrative.Should().BeTrue();
        vm.NarrativeIsAi.Should().BeTrue();
        vm.Narrative.Should().Be("Rata wychodzi 10-tego.");
        explainer.LastProjection.Should().NotBeNull();
        explainer.LastProjection!.Events.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ChangingHorizon_ClearsStaleNarrative()
    {
        var repo = new FakeRecurringFlowRepository(MonthlyOutflow("Rata", 500m, day: 10));
        var vm = CreateViewModel(repo, new FakeCashFlowExplainer());
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ExplainCommand.ExecuteAsync(null);
        vm.HasNarrative.Should().BeTrue();

        vm.SelectedHorizon = vm.HorizonOptions[3];

        vm.HasNarrative.Should().BeFalse("re-projecting invalidates the prior narration");
        vm.Narrative.Should().BeEmpty();
    }

    private static CashFlowPlanningViewModel CreateViewModel(
        FakeRecurringFlowRepository repo,
        out FakeRecurringFlowDetector detector,
        out FakeRunningBalanceQuery balance,
        out FakeStatementContinuityChecker continuity)
    {
        detector = new FakeRecurringFlowDetector();
        balance = new FakeRunningBalanceQuery();
        continuity = new FakeStatementContinuityChecker();

        return new CashFlowPlanningViewModel(
            repo,
            detector,
            balance,
            continuity,
            new FakePlanningSettings(),
            new CashFlowProjectionEngine(),
            new FakeCashFlowExplainer(),
            new FakeLocalizer(),
            NullLogger<CashFlowPlanningViewModel>.Instance);
    }

    private static CashFlowPlanningViewModel CreateViewModel(
        FakeRecurringFlowRepository repo,
        FakeCashFlowExplainer explainer)
    {
        return new CashFlowPlanningViewModel(
            repo,
            new FakeRecurringFlowDetector(),
            new FakeRunningBalanceQuery(),
            new FakeStatementContinuityChecker(),
            new FakePlanningSettings(),
            new CashFlowProjectionEngine(),
            explainer,
            new FakeLocalizer(),
            NullLogger<CashFlowPlanningViewModel>.Instance);
    }

    private static RecurringFlow MonthlyOutflow(string name, decimal amount, int day) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Direction = FlowDirection.Outflow,
        IntervalMonths = 1,
        AnchorDayOfMonth = day,
        TypicalAmount = amount,
        Currency = "PLN",
        IsActive = true,
        Source = FlowSource.Manual,
        CreatedAt = DateTime.UtcNow,
    };

    private static RecurringFlowCandidate Candidate(string name, string merchant) => new(
        name,
        FlowDirection.Outflow,
        merchant,
        null,
        1,
        5,
        null,
        43m,
        2m,
        6);
}
