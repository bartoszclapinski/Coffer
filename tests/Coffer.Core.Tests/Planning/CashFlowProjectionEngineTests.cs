using Coffer.Core.Domain;
using Coffer.Core.Planning;
using FluentAssertions;

namespace Coffer.Core.Tests.Planning;

public class CashFlowProjectionEngineTests
{
    private readonly CashFlowProjectionEngine _engine = new();

    private static RecurringFlow Flow(
        FlowDirection direction,
        decimal amount,
        int anchorDay,
        int intervalMonths = 1,
        int? anchorMonth = null,
        int accrualOffsetMonths = 0,
        bool isActive = true) => new()
        {
            Id = Guid.NewGuid(),
            Name = direction == FlowDirection.Inflow ? "Salary" : "Bill",
            Direction = direction,
            IntervalMonths = intervalMonths,
            AnchorDayOfMonth = anchorDay,
            AnchorMonth = anchorMonth,
            TypicalAmount = amount,
            AccrualOffsetMonths = accrualOffsetMonths,
            Currency = "PLN",
            IsActive = isActive,
            Source = FlowSource.Manual,
            CreatedAt = DateTime.UtcNow,
        };

    [Fact]
    public void Monthly_ExpandsEveryMonthInHorizon()
    {
        var flow = Flow(FlowDirection.Outflow, 100m, anchorDay: 10);

        var projection = _engine.Project([flow], openingBalance: 1000m, from: new DateOnly(2026, 1, 1), horizonDays: 90);

        projection.Events.Select(e => e.Date).Should().Equal(
            new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 10), new DateOnly(2026, 3, 10));
    }

    [Fact]
    public void Quarterly_ExpandsEveryThreeMonthsFromAnchorMonth()
    {
        var flow = Flow(FlowDirection.Outflow, 300m, anchorDay: 15, intervalMonths: 3, anchorMonth: 1);

        var projection = _engine.Project([flow], openingBalance: 0m, from: new DateOnly(2026, 1, 1), horizonDays: 365);

        projection.Events.Select(e => e.Date).Should().Equal(
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 4, 15),
            new DateOnly(2026, 7, 15),
            new DateOnly(2026, 10, 15));
    }

    [Fact]
    public void Yearly_ExpandsOncePerYearOnAnchorMonth()
    {
        var flow = Flow(FlowDirection.Outflow, 1200m, anchorDay: 5, intervalMonths: 12, anchorMonth: 6);

        var projection = _engine.Project([flow], openingBalance: 0m, from: new DateOnly(2026, 1, 1), horizonDays: 800);

        projection.Events.Select(e => e.Date).Should().Equal(
            new DateOnly(2026, 6, 5), new DateOnly(2027, 6, 5));
    }

    [Fact]
    public void AnchorDay_ClampsToShortMonths()
    {
        var flow = Flow(FlowDirection.Outflow, 50m, anchorDay: 31);

        var projection = _engine.Project([flow], openingBalance: 0m, from: new DateOnly(2026, 2, 1), horizonDays: 28);

        projection.Events.Should().ContainSingle();
        projection.Events[0].Date.Should().Be(new DateOnly(2026, 2, 28));
    }

    [Fact]
    public void RunningBalance_AccumulatesSignedAmounts()
    {
        var salary = Flow(FlowDirection.Inflow, 5000m, anchorDay: 1);
        var rent = Flow(FlowDirection.Outflow, 2000m, anchorDay: 10);

        var projection = _engine.Project([salary, rent], openingBalance: 100m, from: new DateOnly(2026, 1, 1), horizonDays: 31);

        projection.OpeningBalance.Should().Be(100m);
        projection.Events[0].BalanceAfter.Should().Be(5100m);  // +5000 salary on the 1st
        projection.Events[1].BalanceAfter.Should().Be(3100m);  // -2000 rent on the 10th
        projection.ClosingBalance.Should().Be(3100m);
    }

    [Fact]
    public void SameDay_AppliesOutflowBeforeInflow()
    {
        var salary = Flow(FlowDirection.Inflow, 1000m, anchorDay: 15);
        var bill = Flow(FlowDirection.Outflow, 400m, anchorDay: 15);

        var projection = _engine.Project([salary, bill], openingBalance: 200m, from: new DateOnly(2026, 1, 1), horizonDays: 31);

        projection.Events[0].Direction.Should().Be(FlowDirection.Outflow);
        projection.Events[0].BalanceAfter.Should().Be(-200m);   // outflow first dips below zero
        projection.Events[1].BalanceAfter.Should().Be(800m);
        projection.LowestBalance.Should().Be(-200m);
        projection.LowestBalanceDate.Should().Be(new DateOnly(2026, 1, 15));
    }

    [Fact]
    public void TightWindow_FlaggedWhenBalanceDropsToOrBelowFloor()
    {
        var bill = Flow(FlowDirection.Outflow, 150m, anchorDay: 10);

        var projection = _engine.Project([bill], openingBalance: 100m, from: new DateOnly(2026, 1, 1), horizonDays: 31, tightFloor: 0m);

        projection.HasTightWindow.Should().BeTrue();
        projection.Events[0].IsTight.Should().BeTrue();
    }

    [Fact]
    public void AccrualPeriod_OffsetsBackByConfiguredMonths()
    {
        var tax = Flow(FlowDirection.Outflow, 500m, anchorDay: 20, accrualOffsetMonths: 1);

        var projection = _engine.Project([tax], openingBalance: 0m, from: new DateOnly(2026, 3, 1), horizonDays: 31);

        projection.Events.Should().ContainSingle();
        projection.Events[0].Date.Should().Be(new DateOnly(2026, 3, 20));
        projection.Events[0].AccrualPeriod.Should().Be(new DateOnly(2026, 2, 1)); // belongs to February
    }

    [Fact]
    public void InactiveFlows_AreExcluded()
    {
        var active = Flow(FlowDirection.Outflow, 100m, anchorDay: 5);
        var inactive = Flow(FlowDirection.Outflow, 999m, anchorDay: 6, isActive: false);

        var projection = _engine.Project([active, inactive], openingBalance: 0m, from: new DateOnly(2026, 1, 1), horizonDays: 31);

        projection.Events.Should().ContainSingle();
        projection.Events[0].Amount.Should().Be(-100m);
    }

    [Fact]
    public void EmptyFlows_ReturnsFlatProjection()
    {
        var projection = _engine.Project([], openingBalance: 750m, from: new DateOnly(2026, 1, 1), horizonDays: 30);

        projection.Events.Should().BeEmpty();
        projection.OpeningBalance.Should().Be(750m);
        projection.ClosingBalance.Should().Be(750m);
        projection.LowestBalance.Should().Be(750m);
        projection.LowestBalanceDate.Should().BeNull();
        projection.HasTightWindow.Should().BeFalse();
    }

    [Fact]
    public void HorizonDays_BelowOne_Throws()
    {
        var act = () => _engine.Project([], openingBalance: 0m, from: new DateOnly(2026, 1, 1), horizonDays: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
