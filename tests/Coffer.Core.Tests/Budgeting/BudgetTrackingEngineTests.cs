using Coffer.Core.Budgeting;
using FluentAssertions;

namespace Coffer.Core.Tests.Budgeting;

public class BudgetTrackingEngineTests
{
    private readonly BudgetTrackingEngine _engine = new();

    [Fact]
    public void UnderBudget_AndOnPace_IsOk()
    {
        // 300 of 1000 spent 10 days into a 30-day month → projected 900, well under.
        var status = _engine.Evaluate(limit: 1000m, spendToDate: 300m, daysElapsed: 10, daysInMonth: 30);

        status.Zone.Should().Be(BudgetZone.Ok);
        status.Remaining.Should().Be(700m);
        status.Projected.Should().Be(900m);
    }

    [Fact]
    public void AtEightyPercentSpent_IsWarning()
    {
        var status = _engine.Evaluate(limit: 1000m, spendToDate: 800m, daysElapsed: 28, daysInMonth: 30);

        status.Zone.Should().Be(BudgetZone.Warning);
    }

    [Fact]
    public void ProjectedToExceed_EvenWhenSpentIsLow_IsWarning()
    {
        // Only 40% spent, but 6 days into a 30-day month → projected 1000 ≥ limit.
        var status = _engine.Evaluate(limit: 500m, spendToDate: 200m, daysElapsed: 6, daysInMonth: 30);

        status.Fraction.Should().BeApproximately(0.4m, 0.001m);
        status.Projected.Should().Be(1000m);
        status.Zone.Should().Be(BudgetZone.Warning);
    }

    [Fact]
    public void AtOrOverLimit_IsOver_WithNegativeRemaining()
    {
        var status = _engine.Evaluate(limit: 1000m, spendToDate: 1200m, daysElapsed: 20, daysInMonth: 30);

        status.Zone.Should().Be(BudgetZone.Over);
        status.Remaining.Should().Be(-200m);
    }

    [Fact]
    public void ZeroDaysElapsed_DoesNotDivideByZero()
    {
        var status = _engine.Evaluate(limit: 1000m, spendToDate: 100m, daysElapsed: 0, daysInMonth: 30);

        // Treated as one day elapsed → projection = 100 * 30.
        status.Projected.Should().Be(3000m);
        // Spent (100) is under the limit, so this is not Over — but the projection blows past it → Warning.
        status.Zone.Should().Be(BudgetZone.Warning);
    }

    [Fact]
    public void MonthEnd_NoLongerProjectsBeyondActual()
    {
        // Full month elapsed, 700 of 1000 → projection equals actual, on pace, under 80%.
        var status = _engine.Evaluate(limit: 1000m, spendToDate: 700m, daysElapsed: 30, daysInMonth: 30);

        status.Projected.Should().Be(700m);
        status.Zone.Should().Be(BudgetZone.Ok);
    }
}
