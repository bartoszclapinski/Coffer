using Coffer.Core.Domain;
using Coffer.Infrastructure.Categorization;
using Coffer.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Categorization;

public class CategoryServiceTests : CategorizationDbTest
{
    private CategoryService Service()
    {
        var categorizer = new RuleCacheCategorizer(Factory, new RuleEngine(NullLogger<RuleEngine>.Instance));
        var cacheStore = new CategoryCacheStore(Factory);
        return new CategoryService(Factory, categorizer, cacheStore);
    }

    [Fact]
    public async Task GetCategories_ExcludesArchived_OrderedByName()
    {
        await using var db = await MigratedContextAsync();
        db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Zdrowie", Color = "#FF3B30" });
        db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Auto", Color = "#007AFF" });
        db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Ukryta", Color = "#000000", IsArchived = true });
        await db.SaveChangesAsync();

        var categories = await Service().GetCategoriesAsync(CancellationToken.None);

        categories.Select(c => c.Name).Should().Equal("Auto", "Zdrowie");
    }

    [Fact]
    public async Task SetCategory_AssignsAndLearnsManualCacheEntry()
    {
        var categoryId = await SeedCategoryAsync("Spożywcze");
        var transactionId = await SeedTransactionAsync("lidl warszawa");

        var normalized = await Service().SetCategoryAsync(transactionId, categoryId, CancellationToken.None);

        normalized.Should().Be("lidl warszawa");

        await using var db = Factory.CreateDbContext();
        (await db.Transactions.SingleAsync(t => t.Id == transactionId)).CategoryId.Should().Be(categoryId);
        var cache = await db.CategoryCache.SingleAsync(c => c.NormalizedDescription == "lidl warszawa");
        cache.CategoryId.Should().Be(categoryId);
        cache.Source.Should().Be(CacheSource.Manual, "a hand correction is learned as Manual");
    }

    [Fact]
    public async Task SetCategory_MissingTransaction_ReturnsNull()
    {
        var categoryId = await SeedCategoryAsync("Spożywcze");

        var normalized = await Service().SetCategoryAsync(Guid.NewGuid(), categoryId, CancellationToken.None);

        normalized.Should().BeNull();
    }

    [Fact]
    public async Task RecategorizeUncategorized_AppliesRulesAndReturnsCount()
    {
        var fuel = await SeedCategoryAsync("Paliwo");
        await SeedRuleAsync("ORLEN", priority: 10, fuel);
        var matched = await SeedTransactionAsync("orlen stacja", hash: "H1");
        var unmatched = await SeedTransactionAsync("unknown merchant", hash: "H2");

        var changed = await Service().RecategorizeUncategorizedAsync(CancellationToken.None);

        changed.Should().Be(1, "only the rule-matched row is categorised");

        await using var db = Factory.CreateDbContext();
        (await db.Transactions.SingleAsync(t => t.Id == matched)).CategoryId.Should().Be(fuel);
        (await db.Transactions.SingleAsync(t => t.Id == unmatched)).CategoryId.Should().BeNull();
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

    private async Task<Guid> SeedTransactionAsync(string normalizedDescription, string hash = "HASH")
    {
        await using var db = await MigratedContextAsync();

        var account = await db.Accounts.FirstOrDefaultAsync();
        if (account is null)
        {
            account = new Account
            {
                Id = Guid.NewGuid(),
                Name = "PKO checking",
                BankCode = "PKO_BP",
                AccountNumber = "PL60102010260000042270201111",
                Currency = "PLN",
                Type = AccountType.Checking,
                CreatedAt = DateTime.UtcNow,
            };
            db.Accounts.Add(account);
        }

        var session = await db.ImportSessions.FirstOrDefaultAsync();
        if (session is null)
        {
            session = new ImportSession
            {
                Id = Guid.NewGuid(),
                FileName = "statement.csv",
                FileHash = "FILEHASH",
                BankCode = "PKO_BP",
                PeriodFrom = new DateOnly(2026, 5, 1),
                PeriodTo = new DateOnly(2026, 5, 31),
                ImportedAt = DateTime.UtcNow,
                Status = ImportStatus.Completed,
            };
            db.ImportSessions.Add(session);
        }

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            ImportSessionId = session.Id,
            Date = new DateOnly(2026, 5, 15),
            Amount = -42.50m,
            Currency = "PLN",
            Description = normalizedDescription,
            NormalizedDescription = normalizedDescription,
            Hash = hash,
            CreatedAt = DateTime.UtcNow,
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.Id;
    }
}
