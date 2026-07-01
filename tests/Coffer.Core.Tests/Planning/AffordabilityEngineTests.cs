using Coffer.Core.Domain;
using Coffer.Core.Planning;
using FluentAssertions;

namespace Coffer.Core.Tests.Planning;

public class AffordabilityEngineTests
{
    private readonly AffordabilityEngine _engine = new(new CashFlowProjectionEngine());

    private static readonly DateOnly _spendDate = new(2026, 1, 5);

    private static BalanceTrust Trusted() =>
        new(IsTrustworthy: true, WindowFrom: _spendDate, Gaps: Array.Empty<StatementGap>());

    private static RecurringFlow Flow(
        string name, FlowDirection direction, decimal amount, int anchorDay) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Direction = direction,
            IntervalMonths = 1,
            AnchorDayOfMonth = anchorDay,
            TypicalAmount = amount,
            Currency = "PLN",
            IsActive = true,
            Source = FlowSource.Manual,
            CreatedAt = DateTime.UtcNow,
        };

    [Fact]
    public void Affordable_WhenLowPointStaysAboveFloor()
    {
        var salary = Flow("Salary", FlowDirection.Inflow, 6000m, anchorDay: 25);
        var rent = Flow("Rent", FlowDirection.Outflow, 2000m, anchorDay: 10);

        var verdict = _engine.Assess(
            spendAmount: 1000m, spendDate: _spendDate, openingBalance: 5000m,
            flows: [salary, rent], dailyBurn: 0m, safetyFloor: 0m, trust: Trusted(), balanceIsRelative: false);

        verdict.CanAfford.Should().BeTrue();
        verdict.LowestBalance.Should().Be(2000m);       // 5000 - 1000 spend - 2000 rent
        verdict.LowestBalanceDate.Should().Be(new DateOnly(2026, 1, 10));
        verdict.Headroom.Should().Be(2000m);
        verdict.NextInflowDate.Should().Be(new DateOnly(2026, 1, 25));
        verdict.Driver!.Name.Should().Be("Rent");
    }

    [Fact]
    public void NotAffordable_WhenSpendPlusOutflowBreachesFloorBeforeSalary()
    {
        var salary = Flow("Salary", FlowDirection.Inflow, 6000m, anchorDay: 25);
        var rent = Flow("Rent", FlowDirection.Outflow, 1500m, anchorDay: 10);

        var verdict = _engine.Assess(
            spendAmount: 2500m, spendDate: _spendDate, openingBalance: 3000m,
            flows: [salary, rent], dailyBurn: 0m, safetyFloor: 1000m, trust: Trusted(), balanceIsRelative: false);

        verdict.CanAfford.Should().BeFalse();
        verdict.LowestBalance.Should().Be(-1000m);      // 3000 - 2500 - 1500
        verdict.Headroom.Should().Be(-2000m);           // -1000 - 1000 floor
        verdict.Driver!.Name.Should().Be("Rent");       // the payment that pushes under
    }

    [Fact]
    public void PureBurn_DrivesLowPoint_DriverIsNull()
    {
        var salary = Flow("Salary", FlowDirection.Inflow, 5000m, anchorDay: 20);

        var verdict = _engine.Assess(
            spendAmount: 500m, spendDate: _spendDate, openingBalance: 2000m,
            flows: [salary], dailyBurn: 50m, safetyFloor: 0m, trust: Trusted(), balanceIsRelative: false);

        // 2000 - 500 spend - (15 days * 50 burn) = 750 just before salary lands on the 20th
        verdict.LowestBalance.Should().Be(750m);
        verdict.LowestBalanceDate.Should().Be(new DateOnly(2026, 1, 20));
        verdict.Driver.Should().BeNull();
        verdict.CanAfford.Should().BeTrue();
        verdict.NextInflowDate.Should().Be(new DateOnly(2026, 1, 20));
    }

    [Fact]
    public void NoInflowInWindow_ProjectsBurnToHorizonEnd()
    {
        var verdict = _engine.Assess(
            spendAmount: 100m, spendDate: _spendDate, openingBalance: 1000m,
            flows: [], dailyBurn: 20m, safetyFloor: 0m, trust: Trusted(), balanceIsRelative: false);

        var horizonEnd = _spendDate.AddDays(AffordabilityEngine.DefaultMaxHorizonDays - 1);
        verdict.NextInflowDate.Should().BeNull();
        verdict.LowestBalanceDate.Should().Be(horizonEnd);
        // 1000 - 100 spend - (91 days * 20 burn) = -920
        verdict.LowestBalance.Should().Be(-920m);
        verdict.CanAfford.Should().BeFalse();
        verdict.Driver.Should().BeNull();
    }

    [Fact]
    public void Uncertain_WhenTrustReportsGapInWindow()
    {
        var gap = new StatementGap(Guid.NewGuid(), new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 4));
        var untrusted = new BalanceTrust(IsTrustworthy: false, WindowFrom: _spendDate, Gaps: [gap]);

        var verdict = _engine.Assess(
            spendAmount: 100m, spendDate: _spendDate, openingBalance: 5000m,
            flows: [], dailyBurn: 0m, safetyFloor: 0m, trust: untrusted, balanceIsRelative: false);

        verdict.IsUncertain.Should().BeTrue();
        verdict.UncertaintyGap.Should().Be(gap);
    }

    [Fact]
    public void Relative_WhenBalanceHasNoAnchor()
    {
        var verdict = _engine.Assess(
            spendAmount: 100m, spendDate: _spendDate, openingBalance: 5000m,
            flows: [], dailyBurn: 0m, safetyFloor: 0m, trust: Trusted(), balanceIsRelative: true);

        verdict.IsRelative.Should().BeTrue();
        verdict.IsUncertain.Should().BeFalse();
    }

    [Fact]
    public void NegativeSpend_Throws()
    {
        var act = () => _engine.Assess(
            spendAmount: -1m, spendDate: _spendDate, openingBalance: 100m,
            flows: [], dailyBurn: 0m, safetyFloor: 0m, trust: Trusted(), balanceIsRelative: false);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
