using Coffer.Core.Ai;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Tests.Categorization;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

/// <summary>
/// The route resolver over the real settings store (Sprint 31-B): each role defaults to Claude with
/// its default model, stored routes are returned per role, and a change is seen on the next call.
/// </summary>
public class AiRouteResolverTests : CategorizationDbTest
{
    [Fact]
    public async Task Resolve_NothingStored_ReturnsTheClaudeDefaultsPerRole()
    {
        var resolver = await ResolverAsync();

        (await resolver.ResolveAsync(AiRole.Categorization, CancellationToken.None))
            .Should().Be(new AiRoute(AiDefaults.ClaudeProvider, AiDefaults.CategorizationModel));
        (await resolver.ResolveAsync(AiRole.Analysis, CancellationToken.None))
            .Should().Be(new AiRoute(AiDefaults.ClaudeProvider, AiDefaults.AnalysisModel));
    }

    [Fact]
    public async Task Resolve_StoredRoutes_ReturnedPerRole()
    {
        var store = await StoreAsync();
        await store.SetProviderAsync(AiRole.Categorization, AiDefaults.OllamaProvider, CancellationToken.None);
        await store.SetModelAsync(AiRole.Categorization, "qwen2.5:3b", CancellationToken.None);
        await store.SetModelAsync(AiRole.Analysis, "claude-sonnet-4-6", CancellationToken.None);
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

        await store.SetModelAsync(AiRole.Analysis, "claude-haiku-4-5", CancellationToken.None);

        (await resolver.ResolveAsync(AiRole.Analysis, CancellationToken.None)).Model.Should().Be("claude-haiku-4-5");
    }

    private async Task<AppSettingsStore> StoreAsync()
    {
        await using (await MigratedContextAsync())
        {
        }

        return new AppSettingsStore(Factory);
    }

    private async Task<AiRouteResolver> ResolverAsync() => new(await StoreAsync());
}
