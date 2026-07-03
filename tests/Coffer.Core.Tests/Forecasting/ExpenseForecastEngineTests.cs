using Coffer.Core.Domain;
using Coffer.Core.Forecasting;
using FluentAssertions;

namespace Coffer.Core.Tests.Forecasting;

public class ExpenseForecastEngineTests
{
    private static readonly DateOnly _month = new(2026, 8, 1);

    [Fact]
    public void Forecast_CombinesFixedAndVariable_AndOrdersByTotal()
    {
        var engine = new ExpenseForecastEngine();
        var inputs = new List<CategoryForecastInput>
        {
            new(Guid.NewGuid(), "Rozrywka", "#00F", 40m, 210m, 250m),
            new(Guid.NewGuid(), "Spożywcze", "#0F0", 0m, 1241m, null),
        };

        var result = engine.Forecast(_month, inputs);

        result.Month.Should().Be(_month);
        result.Categories.Should().HaveCount(2);
        result.Categories[0].CategoryName.Should().Be("Spożywcze", "largest total comes first");
        result.Categories[0].Total.Should().Be(1241m);
        result.Categories[1].Total.Should().Be(250m);
        result.Total.Should().Be(1491m);
    }

    [Fact]
    public void Forecast_RoundsSuggestedLimitUpToNearestTen()
    {
        var engine = new ExpenseForecastEngine();
        var inputs = new List<CategoryForecastInput>
        {
            new(Guid.NewGuid(), "A", null, 0m, 1241m, null),   // -> 1250
            new(Guid.NewGuid(), "B", null, 0m, 250m, null),    // -> 250 (already a multiple)
        };

        var result = engine.Forecast(_month, inputs);

        result.Categories.Single(c => c.CategoryName == "A").SuggestedLimit.Should().Be(1250m);
        result.Categories.Single(c => c.CategoryName == "B").SuggestedLimit.Should().Be(250m);
    }

    [Fact]
    public void Forecast_DropsZeroTotalLines()
    {
        var engine = new ExpenseForecastEngine();
        var inputs = new List<CategoryForecastInput>
        {
            new(Guid.NewGuid(), "Empty", null, 0m, 0m, 500m),
            new(Guid.NewGuid(), "Real", null, 100m, 0m, null),
        };

        var result = engine.Forecast(_month, inputs);

        result.Categories.Should().ContainSingle();
        result.Categories[0].CategoryName.Should().Be("Real");
    }

    [Fact]
    public void Forecast_CarriesCurrentLimit()
    {
        var engine = new ExpenseForecastEngine();
        var inputs = new List<CategoryForecastInput>
        {
            new(Guid.NewGuid(), "Spożywcze", "#0F0", 0m, 800m, 1000m),
        };

        var result = engine.Forecast(_month, inputs);

        var line = result.Categories.Single();
        line.CurrentLimit.Should().Be(1000m);
        line.SuggestedLimit.Should().Be(800m);
    }
}
