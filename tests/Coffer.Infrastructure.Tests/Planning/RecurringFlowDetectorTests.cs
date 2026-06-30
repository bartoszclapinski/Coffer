using Coffer.Core.Domain;
using Coffer.Infrastructure.Planning;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Planning;

public class RecurringFlowDetectorTests : PlanningDbTestBase
{
    [Fact]
    public async Task EmptyHistory_ReturnsNoCandidates()
    {
        var candidates = await new RecurringFlowDetector(Factory).DetectAsync(default);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task RecurringMerchantAcrossThreeMonths_BecomesOutflowCandidate()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 4, 10), -49m, "Netflix"),
            NewTransaction(account, session, new DateOnly(2026, 5, 12), -49m, "Netflix"),
            NewTransaction(account, session, new DateOnly(2026, 6, 14), -49m, "Netflix"),
        });

        var candidates = await new RecurringFlowDetector(Factory).DetectAsync(default);

        candidates.Should().ContainSingle();
        var candidate = candidates[0];
        candidate.Name.Should().Be("Netflix");
        candidate.Direction.Should().Be(FlowDirection.Outflow);
        candidate.IntervalMonths.Should().Be(1);
        candidate.MonthsObserved.Should().Be(3);
        candidate.TypicalAmount.Should().Be(49m);
        candidate.AnchorDayOfMonth.Should().Be(12); // median of 10, 12, 14
    }

    [Fact]
    public async Task MerchantSeenInTooFewMonths_IsSkipped()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 30));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 5, 10), -49m, "Netflix"),
            NewTransaction(account, session, new DateOnly(2026, 6, 12), -49m, "Netflix"),
        });

        var candidates = await new RecurringFlowDetector(Factory).DetectAsync(default);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task IncomingMerchant_BecomesInflowCandidate()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 4, 28), 8000m, "Pracodawca"),
            NewTransaction(account, session, new DateOnly(2026, 5, 28), 8000m, "Pracodawca"),
            NewTransaction(account, session, new DateOnly(2026, 6, 28), 8000m, "Pracodawca"),
        });

        var candidates = await new RecurringFlowDetector(Factory).DetectAsync(default);

        candidates.Should().ContainSingle();
        candidates[0].Direction.Should().Be(FlowDirection.Inflow);
        candidates[0].TypicalAmount.Should().Be(8000m);
    }
}
