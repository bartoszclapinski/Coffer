using Coffer.Core.Domain;
using Coffer.Infrastructure.Planning;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Planning;

public class VariableBurnQueryTests : PlanningDbTestBase
{
    // A 90-day trailing window: 2026-01-01 .. 2026-04-01.
    private static readonly DateOnly _asOf = new(2026, 4, 1);

    private async Task SeedFlowAsync(string? matchMerchant = null, Guid? matchCategoryId = null)
    {
        await using var db = Factory.CreateDbContext();
        db.RecurringFlows.Add(new RecurringFlow
        {
            Id = Guid.NewGuid(),
            Name = "Leasing",
            Direction = FlowDirection.Outflow,
            MatchMerchant = matchMerchant,
            MatchCategoryId = matchCategoryId,
            IntervalMonths = 1,
            AnchorDayOfMonth = 10,
            TypicalAmount = 900m,
            Currency = "PLN",
            IsActive = true,
            Source = FlowSource.Detected,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedCategoryAsync(string name)
    {
        var id = Guid.NewGuid();
        await using var db = Factory.CreateDbContext();
        db.Categories.Add(new Category { Id = id, Name = name });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task AveragesDiscretionaryOutflowsOverWindow()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 2, 10), -900m, merchant: "Grocery"),
            NewTransaction(account, session, new DateOnly(2026, 3, 10), -900m, merchant: "Fuel"),
        });

        var burn = await new VariableBurnQuery(Factory).GetDailyBurnAsync(account.Id, _asOf, default);

        burn.Should().Be(20m); // 1800 / 90 days
    }

    [Fact]
    public async Task ExcludesTransactionsMatchingAnActiveFlowMerchant()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1));
        await SeedFlowAsync(matchMerchant: "Leasing");
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 2, 10), -900m, merchant: "Leasing"), // recurring
            NewTransaction(account, session, new DateOnly(2026, 3, 10), -900m, merchant: "Grocery"), // discretionary
        });

        var burn = await new VariableBurnQuery(Factory).GetDailyBurnAsync(account.Id, _asOf, default);

        burn.Should().Be(10m); // only the 900 Grocery counted: 900 / 90
    }

    [Fact]
    public async Task ExcludesTransactionsMatchingAnActiveFlowCategory()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1));
        var recurringCategory = await SeedCategoryAsync("Leasing");
        await SeedFlowAsync(matchCategoryId: recurringCategory);
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 2, 10), -900m, categoryId: recurringCategory), // recurring
            NewTransaction(account, session, new DateOnly(2026, 3, 10), -450m, merchant: "Grocery"), // discretionary
        });

        var burn = await new VariableBurnQuery(Factory).GetDailyBurnAsync(account.Id, _asOf, default);

        burn.Should().Be(5m); // 450 / 90
    }

    [Fact]
    public async Task IgnoresInflowsAndTransactionsOutsideTheWindow()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2025, 12, 1), new DateOnly(2026, 4, 1));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(account, session, new DateOnly(2026, 2, 10), 5000m, merchant: "Salary"),   // inflow
            NewTransaction(account, session, new DateOnly(2025, 12, 20), -900m, merchant: "Old"),     // before window
        });

        var burn = await new VariableBurnQuery(Factory).GetDailyBurnAsync(account.Id, _asOf, default);

        burn.Should().Be(0m);
    }

    [Fact]
    public async Task ScopesToTheGivenAccount()
    {
        var mine = NewAccount();
        var other = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1));
        await SeedTransactionsAsync(new[]
        {
            NewTransaction(other, session, new DateOnly(2026, 2, 10), -1800m, merchant: "Grocery"),
        });

        var burn = await new VariableBurnQuery(Factory).GetDailyBurnAsync(mine.Id, _asOf, default);

        burn.Should().Be(0m); // the other account's spend never leaks in
    }
}
