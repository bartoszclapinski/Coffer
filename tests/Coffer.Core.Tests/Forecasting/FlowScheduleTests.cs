using Coffer.Core.Domain;
using Coffer.Core.Forecasting;
using FluentAssertions;

namespace Coffer.Core.Tests.Forecasting;

public class FlowScheduleTests
{
    private static RecurringFlow Flow(int intervalMonths, int? anchorMonth) => new()
    {
        Id = Guid.NewGuid(),
        Direction = FlowDirection.Outflow,
        IntervalMonths = intervalMonths,
        AnchorMonth = anchorMonth,
        AnchorDayOfMonth = 15,
        TypicalAmount = 100m,
        IsActive = true,
    };

    [Fact]
    public void Monthly_OccursEveryMonth()
    {
        var flow = Flow(1, null);

        FlowSchedule.OccursInMonth(flow, new DateOnly(2026, 7, 1)).Should().BeTrue();
        FlowSchedule.OccursInMonth(flow, new DateOnly(2026, 8, 1)).Should().BeTrue();
    }

    [Fact]
    public void Quarterly_OccursOnlyOnCadencePhase()
    {
        var flow = Flow(3, anchorMonth: 1); // Jan / Apr / Jul / Oct

        FlowSchedule.OccursInMonth(flow, new DateOnly(2026, 7, 1)).Should().BeTrue();
        FlowSchedule.OccursInMonth(flow, new DateOnly(2026, 8, 1)).Should().BeFalse();
        FlowSchedule.OccursInMonth(flow, new DateOnly(2026, 10, 1)).Should().BeTrue();
    }

    [Fact]
    public void Yearly_OccursOnlyInAnchorMonth()
    {
        var flow = Flow(12, anchorMonth: 3); // March only

        FlowSchedule.OccursInMonth(flow, new DateOnly(2026, 3, 1)).Should().BeTrue();
        FlowSchedule.OccursInMonth(flow, new DateOnly(2026, 7, 1)).Should().BeFalse();
        FlowSchedule.OccursInMonth(flow, new DateOnly(2027, 3, 1)).Should().BeTrue();
    }
}
