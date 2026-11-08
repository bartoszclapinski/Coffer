using Coffer.Core.Ai;
using Coffer.Core.Domain;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Tests.Categorization;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

public class AppSettingsStoreTests : CategorizationDbTest
{
    [Fact]
    public async Task Get_BeforeAnySet_ReturnsDefaults()
    {
        var store = await StoreAsync();

        (await store.GetMonthlyCapPlnAsync(CancellationToken.None)).Should().Be(AiDefaults.MonthlyCapPln);
        (await store.GetRouteAsync(AiRole.Categorization, CancellationToken.None))
            .Should().Be(new AiRoute(AiDefaults.ClaudeProvider, AiDefaults.CategorizationModel));
        (await store.GetRouteAsync(AiRole.Analysis, CancellationToken.None))
            .Should().Be(new AiRoute(AiDefaults.ClaudeProvider, AiDefaults.AnalysisModel));
        (await store.GetAiFallbackParsingEnabledAsync(CancellationToken.None)).Should().Be(AiDefaults.AiFallbackParsingEnabled);
        (await store.GetOwnerIdentityNamesAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task SetThenGet_RoundTripsValues()
    {
        var store = await StoreAsync();

        await store.SetMonthlyCapPlnAsync(35.50m, CancellationToken.None);
        await store.SetRouteAsync(
            AiRole.Categorization, new AiRoute(AiDefaults.ClaudeProvider, "claude-sonnet-4-6"), CancellationToken.None);

        (await store.GetMonthlyCapPlnAsync(CancellationToken.None)).Should().Be(35.50m);
        (await store.GetRouteAsync(AiRole.Categorization, CancellationToken.None))
            .Should().Be(new AiRoute(AiDefaults.ClaudeProvider, "claude-sonnet-4-6"));
    }

    [Fact]
    public async Task Routes_PreSprint31Keys_AreTheCategorizationRoute()
    {
        var store = await StoreAsync(("ai.activeProvider", "Claude"), ("ai.categorizationModel", "claude-haiku-4-5"));

        (await store.GetRouteAsync(AiRole.Categorization, CancellationToken.None)).Model.Should().Be("claude-haiku-4-5");
        (await store.GetRouteAsync(AiRole.Analysis, CancellationToken.None)).Model
            .Should().Be(AiDefaults.AnalysisModel, "the analysis route has its own keys and is untouched");
    }

    [Fact]
    public async Task Routes_EachRoleRoundTripsIndependently()
    {
        var store = await StoreAsync();

        await store.SetRouteAsync(
            AiRole.Categorization, new AiRoute(AiDefaults.OllamaProvider, "qwen2.5:3b"), CancellationToken.None);
        await store.SetRouteAsync(
            AiRole.Analysis, new AiRoute(AiDefaults.ClaudeProvider, "claude-sonnet-4-6"), CancellationToken.None);

        (await store.GetRouteAsync(AiRole.Categorization, CancellationToken.None))
            .Should().Be(new AiRoute(AiDefaults.OllamaProvider, "qwen2.5:3b"), "a non-Claude model is not checked against the Claude list");
        (await store.GetRouteAsync(AiRole.Analysis, CancellationToken.None))
            .Should().Be(new AiRoute(AiDefaults.ClaudeProvider, "claude-sonnet-4-6"));
    }

    [Fact]
    public async Task SetRoute_WritesProviderAndModelInOneSave_AndUpsertsSingleRows()
    {
        var store = await StoreAsync();

        await store.SetRouteAsync(
            AiRole.Analysis, new AiRoute(AiDefaults.OllamaProvider, "qwen3.5:latest"), CancellationToken.None);
        await store.SetRouteAsync(
            AiRole.Analysis, new AiRoute(AiDefaults.ClaudeProvider, "claude-haiku-4-5"), CancellationToken.None);

        (await store.GetRouteAsync(AiRole.Analysis, CancellationToken.None))
            .Should().Be(new AiRoute(AiDefaults.ClaudeProvider, "claude-haiku-4-5"));

        await using var db = Factory.CreateDbContext();
        db.AppSettings.Count(s => s.Key == "ai.analysisProvider").Should().Be(1);
        db.AppSettings.Count(s => s.Key == "ai.analysisModel").Should().Be(1);
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("openai")]
    [InlineData("")]
    public async Task GetRoute_RetiredOrEmptyProvider_ReadsAsClaudeWithTheDefaultModel(string stored)
    {
        var store = await StoreAsync(("ai.activeProvider", stored), ("ai.categorizationModel", "gpt-4o-mini"));

        (await store.GetRouteAsync(AiRole.Categorization, CancellationToken.None))
            .Should().Be(new AiRoute(AiDefaults.ClaudeProvider, AiDefaults.CategorizationModel));
    }

    [Fact]
    public async Task GetRoute_UnknownProvider_IsKeptSoTheRouterFailsClosed()
    {
        // A value written by another build (e.g. a future local provider) must not be read as Claude:
        // that would send a prompt meant for a local model to a remote vendor.
        var store = await StoreAsync(("ai.analysisProvider", "LocalFuture"), ("ai.analysisModel", "some-model"));

        (await store.GetRouteAsync(AiRole.Analysis, CancellationToken.None))
            .Should().Be(new AiRoute("LocalFuture", "some-model"));
    }

    [Fact]
    public async Task GetRoute_KnownProviderInOtherCase_ReadsCanonical()
    {
        var store = await StoreAsync(("ai.analysisProvider", "ollama"), ("ai.analysisModel", "qwen2.5:3b"));

        (await store.GetRouteAsync(AiRole.Analysis, CancellationToken.None)).Provider.Should().Be(AiDefaults.OllamaProvider);
    }

    [Fact]
    public async Task GetRoute_NonClaudeProviderWithoutModel_ReadsEmptyModel()
    {
        var store = await StoreAsync(("ai.analysisProvider", AiDefaults.OllamaProvider));

        (await store.GetRouteAsync(AiRole.Analysis, CancellationToken.None)).Model
            .Should().BeEmpty("there is no default local model; an empty model makes the route not configured");
    }

    [Theory]
    [InlineData("gpt-4o-mini")]
    [InlineData("gpt-4o")]
    [InlineData("claude-3-haiku-20240307")]
    public async Task GetRoute_ClaudeModelNoLongerOffered_ReturnsTheRoleDefault(string stored)
    {
        var store = await StoreAsync(("ai.categorizationModel", stored), ("ai.analysisModel", stored));

        (await store.GetRouteAsync(AiRole.Categorization, CancellationToken.None)).Model.Should().Be(AiDefaults.CategorizationModel);
        (await store.GetRouteAsync(AiRole.Analysis, CancellationToken.None)).Model.Should().Be(AiDefaults.AnalysisModel);
    }

    [Fact]
    public async Task GetRoute_EveryOfferedClaudeModel_RoundTripsForBothRoles()
    {
        var store = await StoreAsync();

        foreach (var role in new[] { AiRole.Categorization, AiRole.Analysis })
        {
            foreach (var model in AiDefaults.ClaudeModels)
            {
                await store.SetRouteAsync(role, new AiRoute(AiDefaults.ClaudeProvider, model), CancellationToken.None);
                (await store.GetRouteAsync(role, CancellationToken.None)).Model.Should().Be(model);
            }
        }
    }

    [Theory]
    [InlineData("OpenAI", "gpt-4o-mini")]
    [InlineData("Unknown", "m")]
    [InlineData("Ollama", "")]
    [InlineData("Ollama", "   ")]
    public async Task SetRoute_UnknownProviderOrEmptyModel_Throws(string provider, string model)
    {
        var store = await StoreAsync();

        var act = () => store.SetRouteAsync(AiRole.Analysis, new AiRoute(provider, model), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetThenGet_AiFallbackParsingAndOwnerName_RoundTrips()
    {
        var store = await StoreAsync();

        await store.SetAiFallbackParsingEnabledAsync(true, CancellationToken.None);
        await store.SetOwnerIdentityNamesAsync("Jan Kowalski", CancellationToken.None);

        (await store.GetAiFallbackParsingEnabledAsync(CancellationToken.None)).Should().BeTrue();
        (await store.GetOwnerIdentityNamesAsync(CancellationToken.None)).Should().Be("Jan Kowalski");
    }

    [Fact]
    public async Task SetOwnerName_Blank_ReadsBackAsNull()
    {
        var store = await StoreAsync();

        await store.SetOwnerIdentityNamesAsync("   ", CancellationToken.None);

        (await store.GetOwnerIdentityNamesAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Set_Twice_UpsertsSingleRow()
    {
        var store = await StoreAsync();

        await store.SetMonthlyCapPlnAsync(10m, CancellationToken.None);
        await store.SetMonthlyCapPlnAsync(15m, CancellationToken.None);

        (await store.GetMonthlyCapPlnAsync(CancellationToken.None)).Should().Be(15m);

        await using var db = Factory.CreateDbContext();
        db.AppSettings.Count(s => s.Key == "ai.monthlyCapPln").Should().Be(1);
    }

    // Seeds raw key/value rows (bypassing SetRouteAsync's validation, as an older or newer build might
    // have written them) and returns a store over the migrated DB.
    private async Task<AppSettingsStore> StoreAsync(params (string Key, string Value)[] rows)
    {
        await using (var db = await MigratedContextAsync())
        {
            foreach (var (key, value) in rows)
            {
                db.AppSettings.Add(new AppSetting { Key = key, Value = value });
            }

            await db.SaveChangesAsync();
        }

        return new AppSettingsStore(Factory);
    }
}
