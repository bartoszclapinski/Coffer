using Coffer.Core.Budgeting;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Budgeting;
using Coffer.Infrastructure.Tests.Planning;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Budgeting;

/// <summary>
/// The <see cref="BudgetTrackingQuery"/> over a real SQLCipher database: it anchors on the latest
/// transaction's month, sums that month's per-category debits, runs budgeted categories through the
/// engine, and lists everything else (including the uncategorised bucket) as unbudgeted — excluding
/// credits and other months, and never blending other accounts when scoped.
/// </summary>
public class BudgetTrackingQueryTests : PlanningDbTestBase
{
    [Fact]
    public async Task Overview_TracksBudgetedCategory_AndListsUnbudgetedIncludingUncategorised()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var groceries = await SeedCategoryAsync("Groceries", "#0F0");
        var fun = await SeedCategoryAsync("Fun", "#00F");
        await SeedBudgetAsync(groceries, 1000m);
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 3, 10), -600m, categoryId: groceries),
            NewTransaction(account, session, new DateOnly(2026, 3, 15), -200m, categoryId: groceries),
            NewTransaction(account, session, new DateOnly(2026, 3, 12), 500m, categoryId: groceries), // credit, excluded
            NewTransaction(account, session, new DateOnly(2026, 3, 11), -100m, categoryId: fun),        // unbudgeted
            NewTransaction(account, session, new DateOnly(2026, 3, 13), -50m, categoryId: null),         // uncategorised
        });

        var overview = await new BudgetTrackingQuery(Factory, new BudgetTrackingEngine())
            .GetOverviewAsync(null, default);

        overview.Month.Should().Be(new DateOnly(2026, 3, 1));
        var budget = overview.Budgets.Should().ContainSingle().Subject;
        budget.CategoryName.Should().Be("Groceries");
        budget.Status.Spent.Should().Be(800m); // credit excluded
        budget.Status.Limit.Should().Be(1000m);

        overview.Unbudgeted.Should().HaveCount(2);
        overview.Unbudgeted.Should().ContainSingle(u => u.CategoryName == "Fun" && u.Spent == 100m);
        overview.Unbudgeted.Should().ContainSingle(u => u.CategoryId == null && u.Spent == 50m);
    }

    [Fact]
    public async Task Overview_ExcludesOtherMonths()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var groceries = await SeedCategoryAsync("Groceries", "#0F0");
        await SeedBudgetAsync(groceries, 1000m);
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 3, 20), -300m, categoryId: groceries), // anchor month
            NewTransaction(account, session, new DateOnly(2026, 2, 10), -900m, categoryId: groceries), // prior month
        });

        var overview = await new BudgetTrackingQuery(Factory, new BudgetTrackingEngine())
            .GetOverviewAsync(null, default);

        overview.Month.Should().Be(new DateOnly(2026, 3, 1));
        overview.Budgets.Single().Status.Spent.Should().Be(300m);
    }

    [Fact]
    public async Task Overview_ScopedToAccount_DoesNotBlendOtherAccounts()
    {
        var first = NewAccount();
        var second = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var groceries = await SeedCategoryAsync("Groceries", "#0F0");
        await SeedBudgetAsync(groceries, 1000m);
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(first, session, new DateOnly(2026, 3, 10), -250m, categoryId: groceries),
            NewTransaction(second, session, new DateOnly(2026, 3, 10), -900m, categoryId: groceries),
        });

        var overview = await new BudgetTrackingQuery(Factory, new BudgetTrackingEngine())
            .GetOverviewAsync(first.Id, default);

        overview.Budgets.Single().Status.Spent.Should().Be(250m);
    }

    private async Task<Guid> SeedCategoryAsync(string name, string color)
    {
        await using var db = Factory.CreateDbContext();
        var category = new Category { Id = Guid.NewGuid(), Name = name, Color = color };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task SeedBudgetAsync(Guid categoryId, decimal limit)
    {
        await using var db = Factory.CreateDbContext();
        db.CategoryBudgets.Add(new CategoryBudget
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            LimitAmount = limit,
            Currency = "PLN",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
