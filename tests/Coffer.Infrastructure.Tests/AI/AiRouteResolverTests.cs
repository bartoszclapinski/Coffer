using Coffer.Core.Ai;
using Coffer.Core.Domain;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Tests.Categorization;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

/// <summary>
/// The route resolver over the real settings store (Sprint 31-B): each role defaults to Claude with
/// its default model, stored routes are returned per role, a change is seen on the next call, and a
/// route without a model is "not configured" instead of being handed to pricing or a provider.
/// </summary>
public class AiRouteResolverTests : CategorizationDbTest
{
    [Fact]
    public async Task Resolve_NothingStored_ReturnsTheClaudeDefaultsPerRole()
    {
        var resolver = new AiRouteResolver(await StoreAsync());

        (await resolver.ResolveAsync(AiRole.Categorization, CancellationToken.None))
            .Should().Be(new AiRoute(AiDefaults.ClaudeProvider, AiDefaults.CategorizationModel));
        (await resolver.ResolveAsync(AiRole.Analysis, CancellationToken.None))
            .Should().Be(new AiRoute(AiDefaults.ClaudeProvider, AiDefaults.AnalysisModel));
    }

    [Fact]
    public async Task Resolve_StoredRoutes_ReturnedPerRole()
    {
        var store = await StoreAsync();
        await store.SetRouteAsync(
            AiRole.Categorization, new AiRoute(AiDefaults.OllamaProvider, "qwen2.5:3b"), CancellationToken.None);
        await store.SetRouteAsync(
            AiRole.Analysis, new AiRoute(AiDefaults.ClaudeProvider, "claude-sonnet-4-6"), CancellationToken.None);
        var resolver = new AiRouteResolver(store);

        (await resolver.ResolveAsync(AiRole.Categorization, CancellationToken.None))
            .Should().Be(new AiRoute(AiDefaults.OllamaProvider, "qwen2.5:3b"));
        (await resolver.ResolveAsync(AiRole.Analysis, CancellationToken.None))
            .Should().Be(new AiRoute(AiDefaults.ClaudeProvider, "claude-sonnet-4-6"));
    }

    [Fact]
    public async Task Resolve_RouteChangedBetweenCalls_NextCallSeesTheChange()
    {
        var store = await StoreAsync();
        var resolver = new AiRouteResolver(store);
        await resolver.ResolveAsync(AiRole.Analysis, CancellationToken.None);

        await store.SetRouteAsync(
            AiRole.Analysis, new AiRoute(AiDefaults.ClaudeProvider, "claude-haiku-4-5"), CancellationToken.None);

        (await resolver.ResolveAsync(AiRole.Analysis, CancellationToken.None)).Model.Should().Be("claude-haiku-4-5");
    }

    [Fact]
    public async Task Resolve_ProviderStoredWithoutModel_ThrowsNotConfigured()
    {
        // The state a Settings screen leaves when Ollama is picked before any model is listed.
        var store = await StoreAsync(("ai.activeProvider", AiDefaults.OllamaProvider));
        var resolver = new AiRouteResolver(store);

        var act = () => resolver.ResolveAsync(AiRole.Categorization, CancellationToken.None);

        (await act.Should().ThrowAsync<AiRouteNotConfiguredException>()).Which.Role.Should().Be(AiRole.Categorization);
    }

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
