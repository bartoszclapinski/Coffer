using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Transactions;
using Coffer.Core.Categorization;
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
        out FakeCategoryService categories,
        IReadOnlyList<TransactionListItem>? items = null,
        IReadOnlyList<AccountListItem>? accounts = null,
        params CategoryListItem[] seedCategories)
    {
        query = new FakeGetTransactionsQuery(items, accounts);
        categories = new FakeCategoryService(seedCategories);
        return new TransactionsViewModel(query, categories, NullLogger<TransactionsViewModel>.Instance);
    }

    [Fact]
    public async Task Load_PopulatesTransactionsAndAccounts()
    {
        var vm = Create(out var query, out _, items: [Tx()], accounts: [_account]);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Transactions.Should().ContainSingle();
        vm.Accounts.Should().Contain(_account);
        vm.Accounts.First().Should().Be(TransactionsViewModel.AllAccounts,
            "the clear-filter sentinel leads the list");
        vm.IsEmpty.Should().BeFalse();
        query.GetAccountsCalls.Should().Be(1);
    }

    [Fact]
    public async Task Load_RefreshesAccountsOnEachNavigation()
    {
        var vm = Create(out var query, out _, accounts: [_account]);

        await vm.LoadCommand.ExecuteAsync(null);
        await vm.LoadCommand.ExecuteAsync(null);

        query.GetAccountsCalls.Should().Be(2,
            "navigating back must pick up accounts created inline on the Import page");
        vm.Accounts.Should().HaveCount(2, "the sentinel is not duplicated on reload");
    }

    [Fact]
    public async Task Load_DefaultsToSixMonthWindow()
    {
        var vm = Create(out var query, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedRange.Should().Be(DateRangeOption.SixMonths);
        var expected = DateOnly.FromDateTime(DateTime.Now).AddMonths(-6);
        query.LastFilter!.From.Should().Be(expected);
    }

    [Fact]
    public async Task Load_WhenEmpty_ReportsEmpty()
    {
        var vm = Create(out _, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Transactions.Should().BeEmpty();
        vm.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task SearchText_ReQueriesWithTrimmedSearch()
    {
        var vm = Create(out var query, out _);
        await vm.LoadCommand.ExecuteAsync(null);
        var before = query.ExecuteCalls;

        vm.SearchText = "  Lidl  ";

        query.ExecuteCalls.Should().Be(before + 1);
        query.LastFilter!.Search.Should().Be("Lidl");
    }

    [Fact]
    public async Task BlankSearchText_ReQueriesWithNullSearch()
    {
        var vm = Create(out var query, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SearchText = "   ";

        query.LastFilter!.Search.Should().BeNull();
    }

    [Fact]
    public async Task SelectedAccount_ReQueriesWithAccountId()
    {
        var vm = Create(out var query, out _, accounts: [_account]);
        await vm.LoadCommand.ExecuteAsync(null);
        var before = query.ExecuteCalls;

        vm.SelectedAccount = _account;

        query.ExecuteCalls.Should().Be(before + 1);
        query.LastFilter!.AccountId.Should().Be(_account.Id);
    }

    [Fact]
    public async Task SentinelAccount_ClearsTheAccountFilter()
    {
        var vm = Create(out var query, out _, accounts: [_account]);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedAccount = _account;

        vm.SelectedAccount = TransactionsViewModel.AllAccounts;

        query.LastFilter!.AccountId.Should().BeNull(
            "selecting the sentinel means 'all accounts', not an account whose id is Guid.Empty");
    }

    [Fact]
    public async Task SelectedRange_AllTime_ReQueriesWithNoLowerBound()
    {
        var vm = Create(out var query, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedRange = DateRangeOption.All;

        query.LastFilter!.From.Should().Be(DateOnly.MinValue);
    }

    [Fact]
    public async Task FilterChangedMidLoad_CoalescesIntoSingleTrailingReload()
    {
        var vm = Create(out var query, out _);
        await vm.LoadCommand.ExecuteAsync(null);
        var before = query.ExecuteCalls;

        // Hold the next query open so a filter can change while the load is genuinely running.
        query.Gate = new TaskCompletionSource();
        var inFlight = vm.ReloadCommand.ExecuteAsync(null);
        query.ExecuteCalls.Should().Be(before + 1, "the gated query has started but not completed");

        vm.SearchText = "Lidl";
        query.ExecuteCalls.Should().Be(before + 1,
            "a filter changed mid-load is coalesced, not issued as a second concurrent query");

        query.Gate.SetResult();
        await inFlight;

        query.ExecuteCalls.Should().Be(before + 2, "exactly one trailing reload runs after the gate releases");
        query.LastFilter!.Search.Should().Be("Lidl", "the trailing reload uses the latest filter values");
        vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Load_PopulatesCategoriesAndFilterSentinel()
    {
        var cat = new CategoryListItem(Guid.NewGuid(), "Spożywcze", "#34C759");
        var vm = Create(out _, out _, seedCategories: cat);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Categories.Should().ContainSingle("the per-row picker list carries no sentinel");
        vm.CategoryFilters.First().Should().Be(TransactionsViewModel.AllCategories,
            "the clear-filter sentinel leads the filter list");
        vm.CategoryFilters.Should().Contain(cat);
    }

    [Fact]
    public async Task SelectedCategoryFilter_ReQueriesWithCategoryId()
    {
        var cat = new CategoryListItem(Guid.NewGuid(), "Spożywcze", "#34C759");
        var vm = Create(out var query, out _, seedCategories: cat);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedCategoryFilter = cat;

        query.LastFilter!.CategoryId.Should().Be(cat.Id);
    }

    [Fact]
    public async Task SentinelCategory_ClearsTheCategoryFilter()
    {
        var cat = new CategoryListItem(Guid.NewGuid(), "Spożywcze", "#34C759");
        var vm = Create(out var query, out _, seedCategories: cat);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedCategoryFilter = cat;

        vm.SelectedCategoryFilter = TransactionsViewModel.AllCategories;

        query.LastFilter!.CategoryId.Should().BeNull(
            "the sentinel means 'all categories', not a category whose id is Guid.Empty");
    }

    [Fact]
    public async Task RowCategoryChange_DrivesManualRecategorisation()
    {
        var cat = new CategoryListItem(Guid.NewGuid(), "Spożywcze", "#34C759");
        var vm = Create(out _, out var categories, items: [Tx()], seedCategories: cat);
        await vm.LoadCommand.ExecuteAsync(null);
        var row = vm.Transactions.Single();

        row.SelectedCategory = cat;

        categories.SetCategoryCalls.Should().Be(1);
        categories.LastTransactionId.Should().Be(row.Id);
        categories.LastCategoryId.Should().Be(cat.Id);
    }

    [Fact]
    public async Task RecategorizeExisting_RunsServiceThenReloads()
    {
        var vm = Create(out var query, out var categories, items: [Tx()]);
        await vm.LoadCommand.ExecuteAsync(null);
        categories.RecategorizeResult = 3;
        var before = query.ExecuteCalls;

        await vm.RecategorizeExistingCommand.ExecuteAsync(null);

        categories.RecategorizeCalls.Should().Be(1);
        query.ExecuteCalls.Should().Be(before + 1, "a reload runs so the freshly categorised rows show");
    }

    [Fact]
    public void Constructor_DoesNotQueryBeforePageIsShown()
    {
        var vm = Create(out var query, out _);

        query.ExecuteCalls.Should().Be(0, "the load is triggered by navigation, not construction");
        vm.SelectedRange.Should().Be(DateRangeOption.SixMonths);
    }
}
