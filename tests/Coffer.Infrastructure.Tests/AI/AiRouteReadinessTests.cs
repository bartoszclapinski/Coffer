using Coffer.Core.Ai;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

/// <summary>
/// Provider-aware readiness (Sprint 31-B): a route is usable without a call when its provider is
/// registered, a model is set, and the provider's own prerequisites hold — Claude needs a stored API
/// key, a provider without prerequisites (the coming local Ollama) needs none.
/// </summary>
public class AiRouteReadinessTests
{
    private static readonly AiRoute _claudeRoute = new(AiDefaults.ClaudeProvider, AiDefaults.AnalysisModel);

    [Fact]
    public async Task IsReady_ClaudeWithStoredKey_True()
    {
        var readiness = new AiRouteReadiness([await ClaudeAsync(apiKey: "sk-test")]);

        (await readiness.IsReadyAsync(_claudeRoute, CancellationToken.None)).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task IsReady_ClaudeWithoutKey_False(string? apiKey)
    {
        var readiness = new AiRouteReadiness([await ClaudeAsync(apiKey)]);

        (await readiness.IsReadyAsync(_claudeRoute, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task IsReady_ProviderWithoutPrerequisites_TrueWithNoKeyAnywhere()
    {
        var readiness = new AiRouteReadiness([await ClaudeAsync(apiKey: null), new KeylessBackend()]);

        (await readiness.IsReadyAsync(new AiRoute(AiDefaults.OllamaProvider, "qwen2.5:3b"), CancellationToken.None))
            .Should().BeTrue("a local provider needs no API key");
    }

    [Fact]
    public async Task IsReady_UnregisteredProvider_False()
    {
        var readiness = new AiRouteReadiness([await ClaudeAsync(apiKey: "sk-test")]);

        (await readiness.IsReadyAsync(new AiRoute(AiDefaults.OllamaProvider, "qwen2.5:3b"), CancellationToken.None))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task IsReady_NoModel_False(string model)
    {
        var readiness = new AiRouteReadiness([new KeylessBackend()]);

        (await readiness.IsReadyAsync(new AiRoute(AiDefaults.OllamaProvider, model), CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ClaudeProvider_CompleteWithoutKey_ThrowsMissingAiKey()
    {
        var claude = await ClaudeAsync(apiKey: null);

        var act = () => claude.CompleteAsync(
            new AiRequest { Prompt = "p", Provider = AiDefaults.ClaudeProvider, Model = AiDefaults.AnalysisModel },
            CancellationToken.None);

        await act.Should().ThrowAsync<MissingAiKeyException>("consumers treat a missing key as 'not configured'");
    }

    private static async Task<ClaudeProvider> ClaudeAsync(string? apiKey)
    {
        var secrets = new InMemorySecretStore();
        if (apiKey is not null)
        {
            await secrets.SetSecretAsync(AiDefaults.ClaudeApiKeySecret, apiKey, CancellationToken.None);
        }

        return new ClaudeProvider(secrets);
    }

    private sealed class KeylessBackend : IAiProviderBackend
    {
        public string ProviderName => AiDefaults.OllamaProvider;

        public Task<bool> IsConfiguredAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<AiResult<string>> CompleteAsync(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AiResult<AiToolTurn>> CompleteWithToolsAsync(AiToolRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
