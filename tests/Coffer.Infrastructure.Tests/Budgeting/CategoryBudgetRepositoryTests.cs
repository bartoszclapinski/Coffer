using Coffer.Core.Domain;
using Coffer.Infrastructure.Budgeting;
using Coffer.Infrastructure.Tests.Planning;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Budgeting;

/// <summary>
/// The <see cref="CategoryBudgetRepository"/> over a real SQLCipher database: upserting the single active
/// budget per category, removing it, joining the category name, and rejecting an unknown category.
/// </summary>
public class CategoryBudgetRepositoryTests : PlanningDbTestBase
{
    [Fact]
    public async Task SetBudget_InsertsAndJoinsCategoryName()
    {
        var category = await SeedCategoryAsync("Groceries", "#00FF00");
        var repo = new CategoryBudgetRepository(Factory);

        await repo.SetBudgetAsync(category, 1500m, "PLN", default);

        var item = (await repo.GetActiveAsync(default)).Should().ContainSingle().Subject;
        item.CategoryId.Should().Be(category);
        item.CategoryName.Should().Be("Groceries");
        item.LimitAmount.Should().Be(1500m);
        item.Currency.Should().Be("PLN");
    }

    [Fact]
    public async Task SetBudget_Twice_UpdatesTheSingleActiveBudget()
    {
        var category = await SeedCategoryAsync("Fun", "#123456");
        var repo = new CategoryBudgetRepository(Factory);

        await repo.SetBudgetAsync(category, 400m, "PLN", default);
        await repo.SetBudgetAsync(category, 550m, "PLN", default);

        var items = await repo.GetActiveAsync(default);
        items.Should().ContainSingle();
        items[0].LimitAmount.Should().Be(550m);
    }

    [Fact]
    public async Task Remove_DeletesTheBudget()
    {
        var category = await SeedCategoryAsync("Fuel", "#FF0000");
        var repo = new CategoryBudgetRepository(Factory);
        await repo.SetBudgetAsync(category, 300m, "PLN", default);

        await repo.RemoveAsync(category, default);

        (await repo.GetActiveAsync(default)).Should().BeEmpty();
    }

    [Fact]
    public async Task SetBudget_UnknownCategory_Throws()
    {
        var repo = new CategoryBudgetRepository(Factory);

        var act = () => repo.SetBudgetAsync(Guid.NewGuid(), 100m, "PLN", default);

        await act.Should().ThrowAsync<InvalidOperationException>();
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
