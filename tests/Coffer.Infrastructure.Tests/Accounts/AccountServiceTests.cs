using Coffer.Infrastructure.Accounts;
using Coffer.Infrastructure.Tests.Planning;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Accounts;

/// <summary>
/// The <see cref="AccountService"/> balance-anchor surface (18-C) over a real SQLCipher database:
/// projecting accounts with their anchor, and setting/clearing the anchor round-trips through EF.
/// </summary>
public class AccountServiceTests : PlanningDbTestBase
{
    [Fact]
    public async Task GetAllWithAnchors_ProjectsTheAnchorFields()
    {
        var account = NewAccount(anchorDate: new DateOnly(2026, 1, 1), anchorBalance: 4210.55m);
        await SeedAccountsAsync(account);

        var result = await new AccountService(Factory).GetAllWithAnchorsAsync(default);

        var item = result.Should().ContainSingle().Subject;
        item.Id.Should().Be(account.Id);
        item.AnchorDate.Should().Be(new DateOnly(2026, 1, 1));
        item.AnchorBalance.Should().Be(4210.55m);
    }

    [Fact]
    public async Task SetBalanceAnchor_PersistsAmountAndDate()
    {
        var account = NewAccount();
        await SeedAccountsAsync(account);
        var service = new AccountService(Factory);

        await service.SetBalanceAnchorAsync(account.Id, 3000m, new DateOnly(2026, 2, 1), default);

        var item = (await service.GetAllWithAnchorsAsync(default)).Single();
        item.AnchorBalance.Should().Be(3000m);
        item.AnchorDate.Should().Be(new DateOnly(2026, 2, 1));
    }

    [Fact]
    public async Task SetBalanceAnchor_WithNulls_ClearsTheAnchor()
    {
        var account = NewAccount(anchorDate: new DateOnly(2026, 1, 1), anchorBalance: 4210.55m);
        await SeedAccountsAsync(account);
        var service = new AccountService(Factory);

        await service.SetBalanceAnchorAsync(account.Id, balance: null, date: null, default);

        var item = (await service.GetAllWithAnchorsAsync(default)).Single();
        item.AnchorBalance.Should().BeNull();
        item.AnchorDate.Should().BeNull();
    }

    [Fact]
    public async Task SetBalanceAnchor_UnknownAccount_Throws()
    {
        var service = new AccountService(Factory);

        var act = () => service.SetBalanceAnchorAsync(Guid.NewGuid(), 1m, new DateOnly(2026, 1, 1), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
