using Coffer.Core.Domain;
using Coffer.Core.Spending;
using Coffer.Infrastructure.Spending;
using Coffer.Infrastructure.Tests.Planning;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Spending;

/// <summary>
/// The <see cref="SpendingExplorerQuery"/> drill-down over a real SQLCipher database: category totals
/// (debits only, boundaries inclusive, per-account), merchant grouping (null/blank collapsed into one
/// unknown bucket), and the transaction leaf narrowed to a category + merchant.
/// </summary>
public class SpendingExplorerQueryTests : PlanningDbTestBase
{
    private static readonly SpendingWindow _wholeYear =
        new(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

    [Fact]
    public async Task GetCategories_SumsDebitsInWindow_AndExcludesCredits()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var groceries = await SeedCategoryAsync("Groceries", "#00FF00");
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 3, 1), -100m, "Lidl", groceries),
            NewTransaction(account, session, new DateOnly(2026, 3, 2), -50m, "Lidl", groceries),
            NewTransaction(account, session, new DateOnly(2026, 3, 3), 200m, "Salary", groceries), // credit, excluded
            NewTransaction(account, session, new DateOnly(2026, 3, 4), -30m, "Kiosk", categoryId: null),
        });

        var result = await new SpendingExplorerQuery(Factory).GetCategoriesAsync(_wholeYear, null, default);

        result.Should().HaveCount(2);
        var top = result[0];
        top.CategoryName.Should().Be("Groceries");
        top.CategoryColor.Should().Be("#00FF00");
        top.Total.Should().Be(150m);
        top.Count.Should().Be(2);
        var uncategorised = result[1];
        uncategorised.CategoryId.Should().BeNull();
        uncategorised.CategoryName.Should().BeNull();
        uncategorised.Total.Should().Be(30m);
        (top.Share + uncategorised.Share).Should().BeApproximately(1m, 0.0001m);
    }

    [Fact]
    public async Task GetCategories_WindowBoundaries_AreInclusive()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 3, 1), -100m), // == From
            NewTransaction(account, session, new DateOnly(2026, 3, 31), -40m), // == To
            NewTransaction(account, session, new DateOnly(2026, 2, 28), -999m), // before
            NewTransaction(account, session, new DateOnly(2026, 4, 1), -999m), // after
        });
        var window = new SpendingWindow(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var result = await new SpendingExplorerQuery(Factory).GetCategoriesAsync(window, null, default);

        result.Should().ContainSingle();
        result[0].Total.Should().Be(140m);
        result[0].Count.Should().Be(2);
    }

    [Fact]
    public async Task GetCategories_ScopedToAccount_DoesNotLeakOtherAccounts()
    {
        var first = NewAccount();
        var second = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(first, session, new DateOnly(2026, 3, 1), -100m),
            NewTransaction(second, session, new DateOnly(2026, 3, 1), -500m),
        });

        var result = await new SpendingExplorerQuery(Factory).GetCategoriesAsync(_wholeYear, first.Id, default);

        result.Should().ContainSingle();
        result[0].Total.Should().Be(100m);
    }

    [Fact]
    public async Task GetMerchants_CollapsesNullAndBlankIntoOneUnknownBucket()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var category = await SeedCategoryAsync("Shopping", "#123456");
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 3, 1), -100m, "Lidl", category),
            NewTransaction(account, session, new DateOnly(2026, 3, 2), -50m, "Lidl", category),
            NewTransaction(account, session, new DateOnly(2026, 3, 3), -20m, merchant: null, category),
            NewTransaction(account, session, new DateOnly(2026, 3, 4), -10m, merchant: "", category),
            NewTransaction(account, session, new DateOnly(2026, 3, 5), -5m, merchant: "   ", category),
        });

        var result = await new SpendingExplorerQuery(Factory).GetMerchantsAsync(_wholeYear, category, null, default);

        result.Should().HaveCount(2);
        result[0].Merchant.Should().Be("Lidl");
        result[0].Total.Should().Be(150m);
        result[0].Count.Should().Be(2);
        var unknown = result[1];
        unknown.Merchant.Should().BeNull();
        unknown.Total.Should().Be(35m);
        unknown.Count.Should().Be(3);
    }

    [Fact]
    public async Task GetTransactions_NarrowsToCategoryAndMerchant()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var groceries = await SeedCategoryAsync("Groceries", "#00FF00");
        var other = await SeedCategoryAsync("Other", "#FF0000");
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 3, 1), -100m, "Lidl", groceries),
            NewTransaction(account, session, new DateOnly(2026, 3, 2), -60m, "Lidl", groceries),
            NewTransaction(account, session, new DateOnly(2026, 3, 3), -30m, "Biedronka", groceries),
            NewTransaction(account, session, new DateOnly(2026, 3, 4), -70m, "Lidl", other),
        });

        var result = await new SpendingExplorerQuery(Factory)
            .GetTransactionsAsync(_wholeYear, groceries, "Lidl", null, default);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(t => t.Merchant == "Lidl" && t.CategoryName == "Groceries");
        result.Select(t => t.Amount).Should().Equal(-60m, -100m); // newest first (date desc)
    }

    [Fact]
    public async Task GetTransactions_UnknownMerchantBucket_ReturnsNullAndBlankOnly()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var category = await SeedCategoryAsync("Shopping", "#123456");
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 3, 1), -20m, merchant: null, category),
            NewTransaction(account, session, new DateOnly(2026, 3, 2), -10m, merchant: "", category),
            NewTransaction(account, session, new DateOnly(2026, 3, 3), -100m, "Lidl", category),
        });

        var result = await new SpendingExplorerQuery(Factory)
            .GetTransactionsAsync(_wholeYear, category, null, null, default);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(t => string.IsNullOrWhiteSpace(t.Merchant));
    }

    private async Task<Guid> SeedCategoryAsync(string name, string color)
    {
        await using var db = Factory.CreateDbContext();
        var category = new Category { Id = Guid.NewGuid(), Name = name, Color = color };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }
}
