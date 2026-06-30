using Coffer.Infrastructure.Planning;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Planning;

public class RunningBalanceQueryTests : PlanningDbTestBase
{
    [Fact]
    public async Task EmptyHistory_ReturnsZero()
    {
        var query = new RunningBalanceQuery(Factory);

        var balance = await query.GetBalanceAsOfAsync(new DateOnly(2026, 6, 30), default);

        balance.Should().Be(0m);
    }

    [Fact]
    public async Task SumsSignedAmountsUpToAndIncludingAsOf()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 1, 10), 5000m),
            NewTransaction(account, session, new DateOnly(2026, 1, 15), -1200m),
            NewTransaction(account, session, new DateOnly(2026, 2, 1), -800m),
            NewTransaction(account, session, new DateOnly(2026, 7, 1), -9999m), // beyond as-of, excluded
        });

        var query = new RunningBalanceQuery(Factory);

        var balance = await query.GetBalanceAsOfAsync(new DateOnly(2026, 6, 30), default);

        balance.Should().Be(3000m);
    }

    [Fact]
    public async Task IncludesTransactionsOnTheAsOfDate()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 1, 31), 250m),
        });

        var query = new RunningBalanceQuery(Factory);

        var balance = await query.GetBalanceAsOfAsync(new DateOnly(2026, 1, 31), default);

        balance.Should().Be(250m);
    }
}
