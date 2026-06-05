using Coffer.Core.Domain;
using Coffer.Infrastructure.Categorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Categorization;

public class RuleCacheCategorizerTests : CategorizationDbTest
{
    private RuleCacheCategorizer Categorizer() =>
        new(Factory, new RuleEngine(NullLogger<RuleEngine>.Instance));

    [Fact]
    public async Task Categorize_CacheHit_TakesPrecedenceOverRules()
    {
        var ruleCategory = await SeedCategoryAsync("Spożywcze");
        var cachedCategory = await SeedCategoryAsync("Restauracje");
        await SeedRuleAsync("LIDL", priority: 10, ruleCategory);
        await SeedCacheAsync("lidl warszawa", cachedCategory, CacheSource.Manual);

        var result = await Categorizer().CategorizeAsync(["lidl warszawa"], CancellationToken.None);

        result["lidl warszawa"].Should().Be(cachedCategory,
            "a cached (manual) mapping wins over the rule scan");
    }

    [Fact]
    public async Task Categorize_RuleMatch_WritesCacheEntryForNextTime()
    {
        var fuel = await SeedCategoryAsync("Paliwo");
        await SeedRuleAsync("ORLEN", priority: 10, fuel);

        var result = await Categorizer().CategorizeAsync(["orlen stacja paliw"], CancellationToken.None);

        result["orlen stacja paliw"].Should().Be(fuel);

        await using var db = Factory.CreateDbContext();
        var entry = await db.CategoryCache.SingleAsync(c => c.NormalizedDescription == "orlen stacja paliw");
        entry.CategoryId.Should().Be(fuel);
        entry.Source.Should().Be(CacheSource.Rule, "a rule hit is learned so the next encounter is a free cache hit");
    }

    [Fact]
    public async Task Categorize_NoCacheNoRule_MapsToNull()
    {
        await using var db = await MigratedContextAsync();

        var result = await Categorizer().CategorizeAsync(["unknown merchant"], CancellationToken.None);

        result["unknown merchant"].Should().BeNull();
        (await db.CategoryCache.AnyAsync()).Should().BeFalse("an unresolved description writes no cache entry");
    }

    private async Task<Guid> SeedCategoryAsync(string name)
    {
        await using var db = await MigratedContextAsync();
        var category = new Category { Id = Guid.NewGuid(), Name = name, Color = "#34C759" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task SeedRuleAsync(string pattern, int priority, Guid categoryId)
    {
        await using var db = await MigratedContextAsync();
        db.Rules.Add(new Rule
        {
            Id = Guid.NewGuid(),
            Pattern = pattern,
            Priority = priority,
            CategoryId = categoryId,
            IsEnabled = true,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedCacheAsync(string normalized, Guid categoryId, CacheSource source)
    {
        await using var db = await MigratedContextAsync();
        db.CategoryCache.Add(new CategoryCache
        {
            Id = Guid.NewGuid(),
            NormalizedDescription = normalized,
            CategoryId = categoryId,
            Source = source,
            HitCount = 1,
            LastUsedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
