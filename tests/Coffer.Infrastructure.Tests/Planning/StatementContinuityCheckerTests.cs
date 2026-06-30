using Coffer.Infrastructure.Planning;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Planning;

public class StatementContinuityCheckerTests : PlanningDbTestBase
{
    [Fact]
    public async Task ContiguousPeriods_ReportNoGaps()
    {
        var account = NewAccount();
        var jan = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var feb = NewImportSession(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, jan, new DateOnly(2026, 1, 10), -10m),
            NewTransaction(account, feb, new DateOnly(2026, 2, 10), -10m),
        });

        var gaps = await new StatementContinuityChecker(Factory).FindGapsAsync(default);

        gaps.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingMonth_ReportsTheUncoveredStretch()
    {
        var account = NewAccount();
        var jan = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var mar = NewImportSession(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, jan, new DateOnly(2026, 1, 10), -10m),
            NewTransaction(account, mar, new DateOnly(2026, 3, 10), -10m),
        });

        var gaps = await new StatementContinuityChecker(Factory).FindGapsAsync(default);

        gaps.Should().ContainSingle();
        gaps[0].AccountId.Should().Be(account.Id);
        gaps[0].From.Should().Be(new DateOnly(2026, 2, 1));
        gaps[0].To.Should().Be(new DateOnly(2026, 2, 28));
    }

    [Fact]
    public async Task OverlappingPeriods_AreNotGaps()
    {
        var account = NewAccount();
        var first = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 20));
        var second = NewImportSession(new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 28));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, first, new DateOnly(2026, 1, 10), -10m),
            NewTransaction(account, second, new DateOnly(2026, 2, 10), -10m),
        });

        var gaps = await new StatementContinuityChecker(Factory).FindGapsAsync(default);

        gaps.Should().BeEmpty();
    }

    [Fact]
    public async Task GapsAreReportedPerAccount()
    {
        var accountA = NewAccount();
        var accountB = NewAccount();
        var jan = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var mar = NewImportSession(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(accountA, jan, new DateOnly(2026, 1, 10), -10m),
            NewTransaction(accountA, mar, new DateOnly(2026, 3, 10), -10m),
            NewTransaction(accountB, jan, new DateOnly(2026, 1, 10), -10m),
        });

        var gaps = await new StatementContinuityChecker(Factory).FindGapsAsync(default);

        gaps.Should().ContainSingle();
        gaps[0].AccountId.Should().Be(accountA.Id);
    }
}
