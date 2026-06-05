using Coffer.Core.Domain;
using Coffer.Infrastructure.Categorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Categorization;

public class DefaultCategorySeedTests : CategorizationDbTest
{
    private DefaultCategorySeed Seed() =>
        new(Factory, NullLogger<DefaultCategorySeed>.Instance);

    [Fact]
    public async Task Seed_OnEmptyDatabase_InsertsCategoriesAndRules()
    {
        await using (var db = await MigratedContextAsync())
        {
            // Migrate only.
        }

        var inserted = await Seed().SeedAsync(CancellationToken.None);

        inserted.Should().BeGreaterThan(0);

        await using var verify = Factory.CreateDbContext();
        (await verify.Categories.CountAsync()).Should().Be(14);
        (await verify.Rules.CountAsync()).Should().Be(8);
        (await verify.Rules.AllAsync(r => r.CategoryId != Guid.Empty))
            .Should().BeTrue("each starter rule resolves to a seeded category");
    }

    [Fact]
    public async Task Seed_RunTwice_IsIdempotent()
    {
        await using (var db = await MigratedContextAsync())
        {
            // Migrate only.
        }

        await Seed().SeedAsync(CancellationToken.None);
        var secondRun = await Seed().SeedAsync(CancellationToken.None);

        secondRun.Should().Be(0, "a second run inserts nothing when the tables are already populated");

        await using var verify = Factory.CreateDbContext();
        (await verify.Categories.CountAsync()).Should().Be(14, "categories are not duplicated");
        (await verify.Rules.CountAsync()).Should().Be(8, "rules are not duplicated");
    }

    [Fact]
    public async Task Seed_MortgageRule_DoesNotMatchCreditCardRepayment()
    {
        await using (var db = await MigratedContextAsync())
        {
            // Migrate only.
        }

        await Seed().SeedAsync(CancellationToken.None);

        await using var verify = Factory.CreateDbContext();
        var rules = await verify.Rules.ToListAsync();
        var mortgageId = await verify.Categories
            .Where(c => c.Name == "Kredyt hipoteczny")
            .Select(c => c.Id)
            .SingleAsync();

        var engine = new RuleEngine(NullLogger<RuleEngine>.Instance);

        engine.Match("SPŁATA KARTY KREDYTOWEJ", rules).Should().NotBe(mortgageId,
            "a credit-card repayment must not be filed under the mortgage category");
        engine.Match("KREDYT GOTÓWKOWY RATA", rules).Should().NotBe(mortgageId,
            "a cash loan instalment is not a mortgage payment");
        engine.Match("RATA KREDYTU HIPOTECZNEGO", rules).Should().Be(mortgageId,
            "an actual mortgage instalment still resolves to the mortgage category");
    }

    [Fact]
    public async Task Seed_DoesNotTouchExistingUserCategories()
    {
        await using (var db = await MigratedContextAsync())
        {
            db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Moja kategoria", Color = "#111111" });
            await db.SaveChangesAsync();
        }

        await Seed().SeedAsync(CancellationToken.None);

        await using var verify = Factory.CreateDbContext();
        (await verify.Categories.CountAsync()).Should().Be(1,
            "the default category set is skipped once the owner has any category of their own");
    }
}
