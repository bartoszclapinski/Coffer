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
        await using (await MigratedContextAsync())
        {
        }

        var store = new AppSettingsStore(Factory);

        (await store.GetMonthlyCapPlnAsync(CancellationToken.None)).Should().Be(AiDefaults.MonthlyCapPln);
        (await store.GetProviderAsync(AiRole.Categorization, CancellationToken.None)).Should().Be(AiDefaults.ClaudeProvider);
        (await store.GetModelAsync(AiRole.Categorization, CancellationToken.None)).Should().Be(AiDefaults.CategorizationModel);
        (await store.GetProviderAsync(AiRole.Analysis, CancellationToken.None)).Should().Be(AiDefaults.ClaudeProvider);
        (await store.GetModelAsync(AiRole.Analysis, CancellationToken.None)).Should().Be(AiDefaults.AnalysisModel);
        (await store.GetAiFallbackParsingEnabledAsync(CancellationToken.None)).Should().Be(AiDefaults.AiFallbackParsingEnabled);
        (await store.GetOwnerIdentityNamesAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task SetThenGet_RoundTripsValues()
    {
        await using (await MigratedContextAsync())
        {
        }

        var store = new AppSettingsStore(Factory);

        await store.SetMonthlyCapPlnAsync(35.50m, CancellationToken.None);
        await store.SetProviderAsync(AiRole.Categorization, AiDefaults.ClaudeProvider, CancellationToken.None);
        await store.SetModelAsync(AiRole.Categorization, "claude-sonnet-4-6", CancellationToken.None);

        (await store.GetMonthlyCapPlnAsync(CancellationToken.None)).Should().Be(35.50m);
        (await store.GetProviderAsync(AiRole.Categorization, CancellationToken.None)).Should().Be(AiDefaults.ClaudeProvider);
        (await store.GetModelAsync(AiRole.Categorization, CancellationToken.None)).Should().Be("claude-sonnet-4-6");
    }

    [Fact]
    public async Task Routes_PreSprint31Keys_AreTheCategorizationRoute()
    {
        await using (var db = await MigratedContextAsync())
        {
            db.AppSettings.Add(new AppSetting { Key = "ai.activeProvider", Value = "Claude" });
            db.AppSettings.Add(new AppSetting { Key = "ai.categorizationModel", Value = "claude-haiku-4-5" });
            await db.SaveChangesAsync();
        }

        var store = new AppSettingsStore(Factory);

        (await store.GetModelAsync(AiRole.Categorization, CancellationToken.None)).Should().Be("claude-haiku-4-5");
        (await store.GetModelAsync(AiRole.Analysis, CancellationToken.None))
            .Should().Be(AiDefaults.AnalysisModel, "the analysis route has its own keys and is untouched");
    }

    [Fact]
    public async Task Routes_EachRoleRoundTripsIndependently()
    {
        await using (await MigratedContextAsync())
        {
        }

        var store = new AppSettingsStore(Factory);

        await store.SetProviderAsync(AiRole.Categorization, AiDefaults.OllamaProvider, CancellationToken.None);
        await store.SetModelAsync(AiRole.Categorization, "qwen2.5:3b", CancellationToken.None);
        await store.SetModelAsync(AiRole.Analysis, "claude-sonnet-4-6", CancellationToken.None);

        (await store.GetProviderAsync(AiRole.Categorization, CancellationToken.None)).Should().Be(AiDefaults.OllamaProvider);
        (await store.GetModelAsync(AiRole.Categorization, CancellationToken.None))
            .Should().Be("qwen2.5:3b", "a non-Claude model is not checked against the Claude list");
        (await store.GetProviderAsync(AiRole.Analysis, CancellationToken.None)).Should().Be(AiDefaults.ClaudeProvider);
        (await store.GetModelAsync(AiRole.Analysis, CancellationToken.None)).Should().Be("claude-sonnet-4-6");
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Retired")]
    public async Task GetProvider_StoredProviderNotKnown_ReadsAsClaudeWithTheDefaultModel(string stored)
    {
        await using (await MigratedContextAsync())
        {
        }

        var store = new AppSettingsStore(Factory);
        await store.SetProviderAsync(AiRole.Categorization, stored, CancellationToken.None);
        await store.SetModelAsync(AiRole.Categorization, "gpt-4o-mini", CancellationToken.None);

        (await store.GetProviderAsync(AiRole.Categorization, CancellationToken.None)).Should().Be(AiDefaults.ClaudeProvider);
        (await store.GetModelAsync(AiRole.Categorization, CancellationToken.None)).Should().Be(AiDefaults.CategorizationModel);
    }

    [Fact]
    public async Task GetProvider_KnownProviderInOtherCase_ReadsCanonical()
    {
        await using (await MigratedContextAsync())
        {
        }

        var store = new AppSettingsStore(Factory);
        await store.SetProviderAsync(AiRole.Analysis, "ollama", CancellationToken.None);

        (await store.GetProviderAsync(AiRole.Analysis, CancellationToken.None)).Should().Be(AiDefaults.OllamaProvider);
    }

    [Fact]
    public async Task GetModel_NonClaudeProviderWithoutModel_ReadsEmpty()
    {
        await using (await MigratedContextAsync())
        {
        }

        var store = new AppSettingsStore(Factory);
        await store.SetProviderAsync(AiRole.Analysis, AiDefaults.OllamaProvider, CancellationToken.None);

        (await store.GetModelAsync(AiRole.Analysis, CancellationToken.None))
            .Should().BeEmpty("there is no default local model; an empty model makes the route not ready");
    }

    [Theory]
    [InlineData("gpt-4o-mini")]
    [InlineData("gpt-4o")]
    [InlineData("claude-3-haiku-20240307")]
    public async Task GetModel_ClaudeModelNoLongerOffered_ReturnsTheRoleDefault(string stored)
    {
        await using (await MigratedContextAsync())
        {
        }

        var store = new AppSettingsStore(Factory);

        await store.SetModelAsync(AiRole.Categorization, stored, CancellationToken.None);
        await store.SetModelAsync(AiRole.Analysis, stored, CancellationToken.None);

        (await store.GetModelAsync(AiRole.Categorization, CancellationToken.None)).Should().Be(AiDefaults.CategorizationModel);
        (await store.GetModelAsync(AiRole.Analysis, CancellationToken.None)).Should().Be(AiDefaults.AnalysisModel);
    }

    [Fact]
    public async Task GetModel_EveryOfferedClaudeModel_RoundTripsForBothRoles()
    {
        await using (await MigratedContextAsync())
        {
        }

        var store = new AppSettingsStore(Factory);

        foreach (var role in new[] { AiRole.Categorization, AiRole.Analysis })
        {
            foreach (var model in AiDefaults.ClaudeModels)
            {
                await store.SetModelAsync(role, model, CancellationToken.None);
                (await store.GetModelAsync(role, CancellationToken.None)).Should().Be(model);
            }
        }
    }

    [Fact]
    public async Task SetThenGet_AiFallbackParsingAndOwnerName_RoundTrips()
    {
        await using (await MigratedContextAsync())
        {
        }

        var store = new AppSettingsStore(Factory);

        await store.SetAiFallbackParsingEnabledAsync(true, CancellationToken.None);
        await store.SetOwnerIdentityNamesAsync("Jan Kowalski", CancellationToken.None);

        (await store.GetAiFallbackParsingEnabledAsync(CancellationToken.None)).Should().BeTrue();
        (await store.GetOwnerIdentityNamesAsync(CancellationToken.None)).Should().Be("Jan Kowalski");
    }

    [Fact]
    public async Task SetOwnerName_Blank_ReadsBackAsNull()
    {
        await using (await MigratedContextAsync())
        {
        }

        var store = new AppSettingsStore(Factory);

        await store.SetOwnerIdentityNamesAsync("   ", CancellationToken.None);

        (await store.GetOwnerIdentityNamesAsync(CancellationToken.None)).Should().BeNull();
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
