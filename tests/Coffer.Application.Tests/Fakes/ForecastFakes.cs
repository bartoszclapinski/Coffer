using Coffer.Core.Forecasting;

namespace Coffer.Application.Tests.Fakes;

/// <summary>In-memory <see cref="IExpenseForecastQuery"/> returning a seeded forecast, recording each call.</summary>
internal sealed class FakeExpenseForecastQuery : IExpenseForecastQuery
{
    public ExpenseForecast Forecast { get; set; } = new(new DateOnly(2026, 8, 1), [], 0m);

    public int Calls { get; private set; }

    public Task<ExpenseForecast> GetForecastAsync(Guid? accountId, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Forecast);
    }
}
