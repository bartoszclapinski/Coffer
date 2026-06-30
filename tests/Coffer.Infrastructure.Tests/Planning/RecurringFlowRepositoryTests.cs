using Coffer.Core.Domain;
using Coffer.Infrastructure.Planning;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Planning;

public class RecurringFlowRepositoryTests : PlanningDbTestBase
{
    private static RecurringFlow Flow(string name, bool isActive = true, int anchorDay = 1) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Direction = FlowDirection.Outflow,
        IntervalMonths = 1,
        AnchorDayOfMonth = anchorDay,
        TypicalAmount = 123.45m,
        AmountStdDev = 6.78m,
        AccrualOffsetMonths = 1,
        Currency = "PLN",
        IsActive = isActive,
        Source = FlowSource.Manual,
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Add_Then_GetAll_RoundTripsAllFields()
    {
        var repo = new RecurringFlowRepository(Factory);
        var flow = Flow("Rent");

        await repo.AddAsync(flow, default);

        var all = await repo.GetAllAsync(default);
        all.Should().ContainSingle();
        var stored = all[0];
        stored.Name.Should().Be("Rent");
        stored.Direction.Should().Be(FlowDirection.Outflow);
        stored.TypicalAmount.Should().Be(123.45m);
        stored.AmountStdDev.Should().Be(6.78m);
        stored.AccrualOffsetMonths.Should().Be(1);
        stored.Source.Should().Be(FlowSource.Manual);
    }

    [Fact]
    public async Task GetAll_OrdersByName()
    {
        var repo = new RecurringFlowRepository(Factory);
        await repo.AddAsync(Flow("Zeta"), default);
        await repo.AddAsync(Flow("Alpha"), default);

        var all = await repo.GetAllAsync(default);

        all.Select(f => f.Name).Should().Equal("Alpha", "Zeta");
    }

    [Fact]
    public async Task GetActive_FiltersInactive_AndOrdersByAnchorDay()
    {
        var repo = new RecurringFlowRepository(Factory);
        await repo.AddAsync(Flow("Late", anchorDay: 20), default);
        await repo.AddAsync(Flow("Early", anchorDay: 5), default);
        await repo.AddAsync(Flow("Off", isActive: false, anchorDay: 1), default);

        var active = await repo.GetActiveAsync(default);

        active.Select(f => f.Name).Should().Equal("Early", "Late");
    }

    [Fact]
    public async Task Update_PersistsChanges()
    {
        var repo = new RecurringFlowRepository(Factory);
        var flow = Flow("Subscription");
        await repo.AddAsync(flow, default);

        flow.TypicalAmount = 999m;
        flow.IsActive = false;
        await repo.UpdateAsync(flow, default);

        var stored = (await repo.GetAllAsync(default)).Single();
        stored.TypicalAmount.Should().Be(999m);
        stored.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_RemovesFlow()
    {
        var repo = new RecurringFlowRepository(Factory);
        var flow = Flow("Gone");
        await repo.AddAsync(flow, default);

        await repo.DeleteAsync(flow.Id, default);

        (await repo.GetAllAsync(default)).Should().BeEmpty();
    }
}
