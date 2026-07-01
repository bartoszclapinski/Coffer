using Coffer.Infrastructure.Planning;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Planning;

public class RunningBalanceQueryTests : PlanningDbTestBase
{
    [Fact]
    public async Task EmptyHistory_ReturnsZero()
    {
        var query = new RunningBalanceQuery(Factory);

        var balance = await query.GetBalanceAsOfAsync(new DateOnly(2026, 6, 30), accountId: null, default);

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

        var balance = await query.GetBalanceAsOfAsync(new DateOnly(2026, 6, 30), accountId: null, default);

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

        var balance = await query.GetBalanceAsOfAsync(new DateOnly(2026, 1, 31), accountId: null, default);

        balance.Should().Be(250m);
    }

    [Fact]
    public async Task AnchoredAccount_ReturnsAnchorPlusPostAnchorDelta()
    {
        var account = NewAccount(anchorDate: new DateOnly(2026, 1, 1), anchorBalance: 4210.55m);
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 1, 1), 999m),  // on the anchor date, excluded
            NewTransaction(account, session, new DateOnly(2026, 1, 10), 5000m),
            NewTransaction(account, session, new DateOnly(2026, 1, 15), -1200m),
            NewTransaction(account, session, new DateOnly(2026, 7, 1), -9999m), // beyond as-of, excluded
        });

        var query = new RunningBalanceQuery(Factory);

        var balance = await query.GetBalanceAsOfAsync(new DateOnly(2026, 6, 30), account.Id, default);

        // 4210.55 + 5000 - 1200 (the 1 Jan and 1 Jul transactions are outside the window)
        balance.Should().Be(8010.55m);
    }

    [Fact]
    public async Task UnanchoredAccount_FallsBackToPerAccountRunningSum()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 1, 10), 5000m),
            NewTransaction(account, session, new DateOnly(2026, 1, 15), -1200m),
        });

        var query = new RunningBalanceQuery(Factory);

        var balance = await query.GetBalanceAsOfAsync(new DateOnly(2026, 6, 30), account.Id, default);

        balance.Should().Be(3800m);
    }

    [Fact]
    public async Task PerAccountScope_DoesNotLeakAnotherAccountsTransactions()
    {
        var first = NewAccount();
        var second = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(first, session, new DateOnly(2026, 1, 10), 1000m),
            NewTransaction(second, session, new DateOnly(2026, 1, 12), 9999m),
        });

        var query = new RunningBalanceQuery(Factory);

        var balance = await query.GetBalanceAsOfAsync(new DateOnly(2026, 6, 30), first.Id, default);

        balance.Should().Be(1000m);
    }
}
