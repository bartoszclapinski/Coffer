using Coffer.Core.Domain;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Categorization;

public class CategorizationSchemaTests : CategorizationDbTest
{
    [Fact]
    public async Task Migrate_CreatesCategorizationTables()
    {
        await using var db = await MigratedContextAsync();

        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")
            .ToListAsync();

        tables.Should().Contain(new[] { "Rules", "CategoryCache" });
    }

    [Fact]
    public async Task Migrate_CreatesExpectedCategorizationIndexes()
    {
        await using var db = await MigratedContextAsync();

        var ruleIndexes = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='Rules'")
            .ToListAsync();
        var cacheIndexes = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='CategoryCache'")
            .ToListAsync();

        ruleIndexes.Should().Contain("IX_Rules_Priority");
        cacheIndexes.Should().Contain("IX_CategoryCache_NormalizedDescription");
    }

    [Fact]
    public async Task NormalizedDescriptionUniqueIndex_RejectsDuplicate()
    {
        await using var db = await MigratedContextAsync();
        var category = new Category { Id = Guid.NewGuid(), Name = "Spożywcze", Color = "#34C759" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        db.CategoryCache.Add(NewCacheEntry("lidl", category.Id));
        await db.SaveChangesAsync();

        db.CategoryCache.Add(NewCacheEntry("lidl", category.Id));
        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "NormalizedDescription is the unique cache key");
    }

    [Fact]
    public async Task CategoryCache_RoundTripsThroughSqlCipher()
    {
        Guid id;
        var category = new Category { Id = Guid.NewGuid(), Name = "Paliwo", Color = "#FF9500" };

        await using (var db = await MigratedContextAsync())
        {
            db.Categories.Add(category);
            var entry = NewCacheEntry("orlen", category.Id);
            id = entry.Id;
            db.CategoryCache.Add(entry);
            await db.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();

        await using (var db = Factory.CreateDbContext())
        {
            var stored = await db.CategoryCache.SingleAsync(c => c.Id == id);
            stored.NormalizedDescription.Should().Be("orlen");
            stored.Source.Should().Be(CacheSource.Rule);
            stored.CategoryId.Should().Be(category.Id);
        }
    }

    private static CategoryCache NewCacheEntry(string normalized, Guid categoryId) => new()
    {
        Id = Guid.NewGuid(),
        NormalizedDescription = normalized,
        CategoryId = categoryId,
        Source = CacheSource.Rule,
        HitCount = 1,
        LastUsedAt = DateTime.UtcNow,
    };
}
