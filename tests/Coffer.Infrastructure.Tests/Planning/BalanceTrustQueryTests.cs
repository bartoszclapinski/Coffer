using Coffer.Infrastructure.Planning;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Planning;

public class BalanceTrustQueryTests : PlanningDbTestBase
{
    private BalanceTrustQuery CreateQuery() =>
        new(Factory, new StatementContinuityChecker(Factory));

    [Fact]
    public async Task ContiguousWindow_IsTrustworthy()
    {
        var account = NewAccount(anchorDate: new DateOnly(2026, 1, 1), anchorBalance: 1000m);
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 1, 10), 500m),
        });

        var trust = await CreateQuery().CheckAsync(account.Id, new DateOnly(2026, 1, 31), default);

        trust.IsTrustworthy.Should().BeTrue();
        trust.Gaps.Should().BeEmpty();
    }

    [Fact]
    public async Task GapInsideWindow_IsNotTrustworthy_AndReportsTheGap()
    {
        var account = NewAccount(anchorDate: new DateOnly(2026, 1, 1), anchorBalance: 1000m);
        var early = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 20));
        var late = NewImportSession(new DateOnly(2026, 1, 25), new DateOnly(2026, 1, 31));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, early, new DateOnly(2026, 1, 10), 500m),
            NewTransaction(account, late, new DateOnly(2026, 1, 27), 200m),
        });

        var trust = await CreateQuery().CheckAsync(account.Id, new DateOnly(2026, 1, 31), default);

        trust.IsTrustworthy.Should().BeFalse();
        trust.Gaps.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                AccountId = account.Id,
                From = new DateOnly(2026, 1, 21),
                To = new DateOnly(2026, 1, 24),
            });
    }

    [Fact]
    public async Task UnanchoredAccount_UsesEarliestTransactionAsWindowStart()
    {
        var account = NewAccount();
        var early = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 20));
        var late = NewImportSession(new DateOnly(2026, 1, 25), new DateOnly(2026, 1, 31));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, early, new DateOnly(2026, 1, 10), 500m),
            NewTransaction(account, late, new DateOnly(2026, 1, 27), 200m),
        });

        var trust = await CreateQuery().CheckAsync(account.Id, new DateOnly(2026, 1, 31), default);

        trust.WindowFrom.Should().Be(new DateOnly(2026, 1, 10));
        trust.IsTrustworthy.Should().BeFalse();
    }

    [Fact]
    public async Task GapForAnotherAccount_DoesNotAffectThisAccount()
    {
        var mine = NewAccount(anchorDate: new DateOnly(2026, 1, 1), anchorBalance: 1000m);
        var other = NewAccount();
        var mineSession = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var otherEarly = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));
        var otherLate = NewImportSession(new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 31));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(mine, mineSession, new DateOnly(2026, 1, 5), 500m),
            NewTransaction(other, otherEarly, new DateOnly(2026, 1, 3), 100m),
            NewTransaction(other, otherLate, new DateOnly(2026, 1, 22), 100m),
        });

        var trust = await CreateQuery().CheckAsync(mine.Id, new DateOnly(2026, 1, 31), default);

        trust.IsTrustworthy.Should().BeTrue();
    }
}
