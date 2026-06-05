using Coffer.Core.Domain;
using Coffer.Infrastructure.Categorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Categorization;

public class CategoryCacheStoreTests : CategorizationDbTest
{
    [Fact]
    public async Task Get_ExactMatch_ReturnsCategory()
    {
        var categoryId = await SeedCategoryAsync();
        var store = new CategoryCacheStore(Factory);
        await store.UpsertAsync("lidl warszawa", categoryId, CacheSource.Rule, CancellationToken.None);

        var hit = await store.GetAsync("lidl warszawa", CancellationToken.None);

        hit.Should().Be(categoryId);
    }

    [Fact]
    public async Task Get_Miss_ReturnsNull()
    {
        await using var db = await MigratedContextAsync();
        var store = new CategoryCacheStore(Factory);

        var hit = await store.GetAsync("never seen", CancellationToken.None);

        hit.Should().BeNull();
    }

    [Fact]
    public async Task Upsert_RepeatedKey_BumpsHitCount()
    {
        var categoryId = await SeedCategoryAsync();
        var store = new CategoryCacheStore(Factory);

        await store.UpsertAsync("zabka", categoryId, CacheSource.Rule, CancellationToken.None);
        await store.UpsertAsync("zabka", categoryId, CacheSource.Rule, CancellationToken.None);

        await using var db = Factory.CreateDbContext();
        var entry = await db.CategoryCache.SingleAsync(c => c.NormalizedDescription == "zabka");
        entry.HitCount.Should().Be(2, "the second upsert reuses the row rather than inserting a duplicate");
    }

    [Fact]
    public async Task Upsert_ManualOverridesRule()
    {
        var ruleCategory = await SeedCategoryAsync("Spożywcze");
        var manualCategory = await SeedCategoryAsync("Restauracje");
        var store = new CategoryCacheStore(Factory);

        await store.UpsertAsync("glovo", ruleCategory, CacheSource.Rule, CancellationToken.None);
        await store.UpsertAsync("glovo", manualCategory, CacheSource.Manual, CancellationToken.None);

        await using var db = Factory.CreateDbContext();
        var entry = await db.CategoryCache.SingleAsync(c => c.NormalizedDescription == "glovo");
        entry.CategoryId.Should().Be(manualCategory);
        entry.Source.Should().Be(CacheSource.Manual);
    }

    [Fact]
    public async Task Upsert_RuleDoesNotOverrideManual()
    {
        var manualCategory = await SeedCategoryAsync("Restauracje");
        var ruleCategory = await SeedCategoryAsync("Spożywcze");
        var store = new CategoryCacheStore(Factory);

        await store.UpsertAsync("glovo", manualCategory, CacheSource.Manual, CancellationToken.None);
        await store.UpsertAsync("glovo", ruleCategory, CacheSource.Rule, CancellationToken.None);

        await using var db = Factory.CreateDbContext();
        var entry = await db.CategoryCache.SingleAsync(c => c.NormalizedDescription == "glovo");
        entry.CategoryId.Should().Be(manualCategory, "a weaker Rule write must not undo a hand correction");
        entry.Source.Should().Be(CacheSource.Manual);
        entry.HitCount.Should().Be(2, "the weaker write still refreshes the reuse counters");
    }

    private async Task<Guid> SeedCategoryAsync(string name = "Spożywcze")
    {
        await using var db = await MigratedContextAsync();
        var category = new Category { Id = Guid.NewGuid(), Name = name, Color = "#34C759" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }
}
