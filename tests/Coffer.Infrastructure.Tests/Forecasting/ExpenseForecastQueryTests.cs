using Coffer.Core.Domain;
using Coffer.Core.Forecasting;
using Coffer.Infrastructure.Forecasting;
using Coffer.Infrastructure.Tests.Planning;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Forecasting;

/// <summary>
/// <see cref="ExpenseForecastQuery"/> over a real SQLCipher database. Anchors on the latest transaction's
/// month (June 2026) and forecasts the next one (July 2026): a monthly flow lands in its category's fixed
/// part, a yearly flow appears only in the month it falls, recurring-merchant history is excluded from the
/// variable estimate, the uncategorised bucket is surfaced, and current budget limits are carried.
/// </summary>
public class ExpenseForecastQueryTests : PlanningDbTestBase
{
    private static readonly Guid _groceries = Guid.NewGuid();
    private static readonly Guid _entertainment = Guid.NewGuid();
    private static readonly Guid _insurance = Guid.NewGuid();
    private static readonly Guid _taxes = Guid.NewGuid();

    [Fact]
    public async Task GetForecast_TargetsNextMonth_WithFixedVariableAndLimits()
    {
        await SeedAsync();

        var forecast = await new ExpenseForecastQuery(Factory, new ExpenseForecastEngine())
            .GetForecastAsync(accountId: null, CancellationToken.None);

        forecast.Month.Should().Be(new DateOnly(2026, 7, 1));

        // Yearly insurance flow lands in July → fixed only.
        var insurance = forecast.Categories.Single(c => c.CategoryId == _insurance);
        insurance.Fixed.Should().Be(600m);
        insurance.Variable.Should().Be(0m);
        insurance.Total.Should().Be(600m);
        insurance.SuggestedLimit.Should().Be(600m);

        // Groceries: variable only (900 over 3 months → 300/mo), carrying its active budget limit.
        var groceries = forecast.Categories.Single(c => c.CategoryId == _groceries);
        groceries.Fixed.Should().Be(0m);
        groceries.Variable.Should().Be(300m);
        groceries.Total.Should().Be(300m);
        groceries.CurrentLimit.Should().Be(1000m);

        // Entertainment: a monthly Netflix flow (fixed 40) + non-Netflix variable (60 over 3 months → 20);
        // the Netflix history is excluded from the variable estimate.
        var entertainment = forecast.Categories.Single(c => c.CategoryId == _entertainment);
        entertainment.Fixed.Should().Be(40m);
        entertainment.Variable.Should().Be(20m);
        entertainment.Total.Should().Be(60m);

        // Uncategorised debit surfaces as the null bucket (90 over 3 months → 30).
        var uncategorised = forecast.Categories.Single(c => c.CategoryId == null);
        uncategorised.Variable.Should().Be(30m);

        // The March-anchored yearly tax flow does NOT land in July → not present.
        forecast.Categories.Should().NotContain(c => c.CategoryId == _taxes);

        forecast.Categories.Should().BeInDescendingOrder(c => c.Total);
        forecast.Total.Should().Be(990m);
    }

    private async Task SeedAsync()
    {
        var account = NewAccount();
        var session = NewImportSession(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        await using var db = Factory.CreateDbContext();

        db.Categories.AddRange(
            new Category { Id = _groceries, Name = "Spożywcze", Color = "#0F0" },
            new Category { Id = _entertainment, Name = "Rozrywka", Color = "#00F" },
            new Category { Id = _insurance, Name = "Ubezpieczenia", Color = "#F0F" },
            new Category { Id = _taxes, Name = "Podatki", Color = "#F00" });

        db.RecurringFlows.AddRange(
            Flow("Netflix", FlowDirection.Outflow, 1, null, "NETFLIX", _entertainment, 40m),
            Flow("PZU", FlowDirection.Outflow, 12, 7, "PZU", _insurance, 600m),        // lands in July
            Flow("US", FlowDirection.Outflow, 12, 3, null, _taxes, 999m),               // March — not July
            Flow("Pensja", FlowDirection.Inflow, 1, null, "PRACODAWCA", null, 5000m));  // inflow — ignored

        db.CategoryBudgets.Add(new CategoryBudget
        {
            Id = Guid.NewGuid(),
            CategoryId = _groceries,
            LimitAmount = 1000m,
            Currency = "PLN",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });

        db.Transactions.AddRange(
            // Groceries variable: 3×300 across the trailing window; the June one is the latest (anchor).
            NewTransaction(account, session, new DateOnly(2026, 4, 10), -300m, "Biedronka", _groceries),
            NewTransaction(account, session, new DateOnly(2026, 5, 10), -300m, "Biedronka", _groceries),
            NewTransaction(account, session, new DateOnly(2026, 6, 20), -300m, "Biedronka", _groceries),
            // Netflix history — must be excluded from the variable estimate.
            NewTransaction(account, session, new DateOnly(2026, 4, 10), -40m, "NETFLIX", _entertainment),
            NewTransaction(account, session, new DateOnly(2026, 5, 10), -40m, "NETFLIX", _entertainment),
            NewTransaction(account, session, new DateOnly(2026, 6, 10), -40m, "NETFLIX", _entertainment),
            // Non-Netflix entertainment variable.
            NewTransaction(account, session, new DateOnly(2026, 6, 10), -60m, "Kino", _entertainment),
            // Uncategorised variable.
            NewTransaction(account, session, new DateOnly(2026, 6, 10), -90m, "Kiosk", categoryId: null));

        await db.SaveChangesAsync();
    }

    private static RecurringFlow Flow(
        string name,
        FlowDirection direction,
        int intervalMonths,
        int? anchorMonth,
        string? matchMerchant,
        Guid? matchCategoryId,
        decimal typicalAmount) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Direction = direction,
            IntervalMonths = intervalMonths,
            AnchorMonth = anchorMonth,
            AnchorDayOfMonth = 15,
            MatchMerchant = matchMerchant,
            MatchCategoryId = matchCategoryId,
            TypicalAmount = typicalAmount,
            Currency = "PLN",
            IsActive = true,
            Source = FlowSource.Manual,
            CreatedAt = DateTime.UtcNow,
        };
}
