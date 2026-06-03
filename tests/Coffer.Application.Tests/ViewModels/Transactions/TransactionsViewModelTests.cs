using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Transactions;
using Coffer.Core.Transactions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Transactions;

public class TransactionsViewModelTests
{
    private static readonly AccountListItem _account = new(Guid.NewGuid(), "Konto PKO", "PKO_BP");

    private static TransactionListItem Tx(decimal amount = -50m, string description = "Zakupy") =>
        new(Guid.NewGuid(), new DateOnly(2026, 1, 15), description, "Lidl", amount, "PLN",
            _account.Id, _account.Name, null, null, null);

    private static TransactionsViewModel Create(
        out FakeGetTransactionsQuery query,
        IReadOnlyList<TransactionListItem>? items = null,
        IReadOnlyList<AccountListItem>? accounts = null)
    {
        query = new FakeGetTransactionsQuery(items, accounts);
        return new TransactionsViewModel(query, NullLogger<TransactionsViewModel>.Instance);
    }

    [Fact]
    public async Task Load_PopulatesTransactionsAndAccounts()
    {
        var vm = Create(out var query, items: [Tx()], accounts: [_account]);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Transactions.Should().ContainSingle();
        vm.Accounts.Should().ContainSingle();
        vm.IsEmpty.Should().BeFalse();
        query.GetAccountsCalls.Should().Be(1);
    }

    [Fact]
    public async Task Load_DefaultsToSixMonthWindow()
    {
        var vm = Create(out var query);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedRange.Should().Be(DateRangeOption.SixMonths);
        var expected = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-6);
        query.LastFilter!.From.Should().Be(expected);
    }

    [Fact]
    public async Task Load_WhenEmpty_ReportsEmpty()
    {
        var vm = Create(out _);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Transactions.Should().BeEmpty();
        vm.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task SearchText_ReQueriesWithTrimmedSearch()
    {
        var vm = Create(out var query);
        await vm.LoadCommand.ExecuteAsync(null);
        var before = query.ExecuteCalls;

        vm.SearchText = "  Lidl  ";

        query.ExecuteCalls.Should().Be(before + 1);
        query.LastFilter!.Search.Should().Be("Lidl");
    }

    [Fact]
    public async Task BlankSearchText_ReQueriesWithNullSearch()
    {
        var vm = Create(out var query);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SearchText = "   ";

        query.LastFilter!.Search.Should().BeNull();
    }

    [Fact]
    public async Task SelectedAccount_ReQueriesWithAccountId()
    {
        var vm = Create(out var query, accounts: [_account]);
        await vm.LoadCommand.ExecuteAsync(null);
        var before = query.ExecuteCalls;

        vm.SelectedAccount = _account;

        query.ExecuteCalls.Should().Be(before + 1);
        query.LastFilter!.AccountId.Should().Be(_account.Id);
    }

    [Fact]
    public async Task SelectedRange_AllTime_ReQueriesWithNoLowerBound()
    {
        var vm = Create(out var query);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedRange = DateRangeOption.All;

        query.LastFilter!.From.Should().Be(DateOnly.MinValue);
    }

    [Fact]
    public void Constructor_DoesNotQueryBeforePageIsShown()
    {
        var vm = Create(out var query);

        query.ExecuteCalls.Should().Be(0, "the load is triggered by navigation, not construction");
        vm.SelectedRange.Should().Be(DateRangeOption.SixMonths);
    }
}
