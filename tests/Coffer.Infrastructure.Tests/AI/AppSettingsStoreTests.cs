using Coffer.Core.Ai;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Tests.Categorization;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

public class AppSettingsStoreTests : CategorizationDbTest
{
    [Fact]
    public async Task Get_BeforeAnySet_ReturnsDefaults()
    {
        await using (await MigratedContextAsync())
        {
        }

        var store = new AppSettingsStore(Factory);

        (await store.GetMonthlyCapPlnAsync(CancellationToken.None)).Should().Be(AiDefaults.MonthlyCapPln);
        (await store.GetActiveProviderAsync(CancellationToken.None)).Should().Be(AiDefaults.ClaudeProvider);
        (await store.GetCategorizationModelAsync(CancellationToken.None)).Should().Be(AiDefaults.CategorizationModel);
    }

    [Fact]
    public async Task SetThenGet_RoundTripsValues()
    {
        await using (await MigratedContextAsync())
        {
        }

        var store = new AppSettingsStore(Factory);

        await store.SetMonthlyCapPlnAsync(35.50m, CancellationToken.None);
        await store.SetActiveProviderAsync(AiDefaults.OpenAiProvider, CancellationToken.None);
        await store.SetCategorizationModelAsync("gpt-4o-mini", CancellationToken.None);

        (await store.GetMonthlyCapPlnAsync(CancellationToken.None)).Should().Be(35.50m);
        (await store.GetActiveProviderAsync(CancellationToken.None)).Should().Be(AiDefaults.OpenAiProvider);
        (await store.GetCategorizationModelAsync(CancellationToken.None)).Should().Be("gpt-4o-mini");
    }

    [Fact]
    public async Task Set_Twice_UpsertsSingleRow()
    {
        await using (await MigratedContextAsync())
        {
        }

        var store = new AppSettingsStore(Factory);

        await store.SetMonthlyCapPlnAsync(10m, CancellationToken.None);
        await store.SetMonthlyCapPlnAsync(15m, CancellationToken.None);

        (await store.GetMonthlyCapPlnAsync(CancellationToken.None)).Should().Be(15m);

        await using var db = Factory.CreateDbContext();
        db.AppSettings.Count(s => s.Key == "ai.monthlyCapPln").Should().Be(1);
    }
}
