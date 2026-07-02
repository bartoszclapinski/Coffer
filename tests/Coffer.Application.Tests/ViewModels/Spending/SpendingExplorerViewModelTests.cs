using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Spending;
using Coffer.Core.Spending;
using Coffer.Core.Transactions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Spending;

public class SpendingExplorerViewModelTests
{
    private static readonly Guid _groceriesId = Guid.NewGuid();

    [Fact]
    public async Task Load_PopulatesAccountsPresetsAndCategories()
    {
        var query = new FakeSpendingExplorerQuery();
        query.Categories.Add(new CategorySpend(_groceriesId, "Groceries", "#0F0", 150m, 0.75m, 3));
        query.Categories.Add(new CategorySpend(null, null, null, 50m, 0.25m, 1));
        var vm = Create(query, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Accounts.Should().HaveCount(2); // all-accounts + one
        vm.Accounts[0].Id.Should().BeNull();
        vm.Presets.Should().HaveCount(6);
        vm.SelectedPreset!.Preset.Should().Be(SpendingWindowPreset.LastMonth);
        vm.Categories.Should().HaveCount(2);
        vm.Categories[0].Label.Should().Be("Groceries");
        vm.Categories[1].Label.Should().Be("Spending.Uncategorized"); // localized fallback (echoed key)
        vm.TotalText.Should().NotBeNullOrEmpty();
        vm.IsCategoriesLevel.Should().BeTrue();
        vm.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public async Task Load_DefaultWindow_IsLastMonth()
    {
        var query = new FakeSpendingExplorerQuery();
        var vm = Create(query, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var expected = SpendingWindowResolver.Resolve(SpendingWindowPreset.LastMonth, today, null, null);
        query.LastWindow.Should().Be(expected);
        vm.IsCustomWindow.Should().BeFalse();
    }

    [Fact]
    public async Task SelectCategory_DrillsToMerchants_AndScopesQuery()
    {
        var query = new FakeSpendingExplorerQuery();
        query.Categories.Add(new CategorySpend(_groceriesId, "Groceries", "#0F0", 150m, 1m, 3));
        query.Merchants.Add(new MerchantSpend("Lidl", 100m, 0.66m, 2));
        query.Merchants.Add(new MerchantSpend(null, 50m, 0.34m, 1));
        var vm = Create(query, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.SelectCategoryCommand.ExecuteAsync(vm.Categories[0]);

        vm.IsMerchantsLevel.Should().BeTrue();
        vm.CanGoBack.Should().BeTrue();
        vm.Merchants.Should().HaveCount(2);
        vm.Merchants[1].Label.Should().Be("Spending.UnknownMerchant");
        query.LastCategoryId.Should().Be(_groceriesId);
    }

    [Fact]
    public async Task SelectMerchant_DrillsToTransactions_AndScopesQuery()
    {
        var query = new FakeSpendingExplorerQuery();
        query.Categories.Add(new CategorySpend(_groceriesId, "Groceries", "#0F0", 150m, 1m, 3));
        query.Merchants.Add(new MerchantSpend("Lidl", 100m, 1m, 2));
        query.Transactions.Add(Tx("Zakupy Lidl", -60m));
        query.Transactions.Add(Tx("Zakupy Lidl", -40m));
        var vm = Create(query, out _);
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.SelectCategoryCommand.ExecuteAsync(vm.Categories[0]);

        await vm.SelectMerchantCommand.ExecuteAsync(vm.Merchants[0]);

        vm.IsTransactionsLevel.Should().BeTrue();
        vm.Transactions.Should().HaveCount(2);
        vm.Transactions[0].AmountText.Should().NotBeNullOrEmpty();
        query.LastMerchant.Should().Be("Lidl");
        query.LastCategoryId.Should().Be(_groceriesId);
    }

    [Fact]
    public async Task Back_PopsFromTransactionsToMerchantsToCategories()
    {
        var query = new FakeSpendingExplorerQuery();
        query.Categories.Add(new CategorySpend(_groceriesId, "Groceries", "#0F0", 150m, 1m, 3));
        query.Merchants.Add(new MerchantSpend("Lidl", 100m, 1m, 2));
        query.Transactions.Add(Tx("Zakupy Lidl", -60m));
        var vm = Create(query, out _);
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.SelectCategoryCommand.ExecuteAsync(vm.Categories[0]);
        await vm.SelectMerchantCommand.ExecuteAsync(vm.Merchants[0]);

        vm.BackCommand.Execute(null);
        vm.IsMerchantsLevel.Should().BeTrue();
        vm.SelectedMerchant.Should().BeNull();
        vm.Transactions.Should().BeEmpty();

        vm.BackCommand.Execute(null);
        vm.IsCategoriesLevel.Should().BeTrue();
        vm.SelectedCategory.Should().BeNull();
        vm.Merchants.Should().BeEmpty();
        vm.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyWindow_Custom_UsesCustomDatesAndReQueries()
    {
        var query = new FakeSpendingExplorerQuery();
        var vm = Create(query, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedPreset = vm.Presets.First(p => p.Preset == SpendingWindowPreset.Custom);
        vm.CustomFrom = new DateTimeOffset(new DateTime(2026, 1, 1), TimeSpan.Zero);
        vm.CustomTo = new DateTimeOffset(new DateTime(2026, 1, 31), TimeSpan.Zero);
        await vm.ApplyWindowCommand.ExecuteAsync(null);

        vm.IsCustomWindow.Should().BeTrue();
        query.LastWindow.Should().Be(new SpendingWindow(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
        vm.IsCategoriesLevel.Should().BeTrue();
    }

    private static SpendingExplorerViewModel Create(FakeSpendingExplorerQuery query, out FakeAccountService accounts)
    {
        accounts = new FakeAccountService();
        accounts.SeedAnchor(Guid.NewGuid(), "PKO", "PKO_BP", null, null);
        return new SpendingExplorerViewModel(
            query,
            accounts,
            new FakeLocalizer(),
            NullLogger<SpendingExplorerViewModel>.Instance);
    }

    private static TransactionListItem Tx(string description, decimal amount) => new(
        Guid.NewGuid(),
        new DateOnly(2026, 1, 15),
        description,
        "Lidl",
        amount,
        "PLN",
        Guid.NewGuid(),
        "PKO",
        _groceriesId,
        "Groceries",
        "#0F0");
}
