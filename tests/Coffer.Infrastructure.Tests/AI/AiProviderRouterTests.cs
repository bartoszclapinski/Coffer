using Coffer.Core.Ai;
using Coffer.Core.Security;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.AI.Ollama;
using Coffer.Infrastructure.DependencyInjection;
using Coffer.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.AI;

/// <summary>
/// The routing <see cref="IAiProvider"/> (Sprint 31-B): each request goes to the backend its
/// <c>Provider</c> names, an unknown provider fails before anything is sent, and DI hands consumers the
/// router over the registered backends.
/// </summary>
public class AiProviderRouterTests
{
    [Fact]
    public async Task CompleteAsync_DispatchesToTheBackendTheRequestNames()
    {
        var claude = new StubBackend(AiDefaults.ClaudeProvider);
        var local = new StubBackend(AiDefaults.OllamaProvider);
        var router = new AiProviderRouter([claude, local]);

        var result = await router.CompleteAsync(Request(AiDefaults.OllamaProvider), CancellationToken.None);

        result.Value.Should().Be(AiDefaults.OllamaProvider);
        local.Calls.Should().Be(1);
        claude.Calls.Should().Be(0);
    }

    [Fact]
    public async Task CompleteJsonAsync_DispatchesToTheBackendTheRequestNames()
    {
        var claude = new StubBackend(AiDefaults.ClaudeProvider);
        var local = new StubBackend(AiDefaults.OllamaProvider);
        var router = new AiProviderRouter([claude, local]);

        var result = await router.CompleteJsonAsync<string>(Request(AiDefaults.ClaudeProvider), CancellationToken.None);

        result.Value.Should().Be(AiDefaults.ClaudeProvider);
        claude.Calls.Should().Be(1);
        local.Calls.Should().Be(0);
    }

    [Fact]
    public async Task CompleteWithToolsAsync_DispatchesToTheBackendTheRequestNames()
    {
        var claude = new StubBackend(AiDefaults.ClaudeProvider);
        var local = new StubBackend(AiDefaults.OllamaProvider);
        var router = new AiProviderRouter([claude, local]);
        var request = new AiToolRequest
        {
            Provider = AiDefaults.OllamaProvider,
            Model = "qwen3.5:latest",
            Messages = [AiChatMessage.User("q")],
        };

        var result = await router.CompleteWithToolsAsync(request, CancellationToken.None);

        result.Value.Text.Should().Be(AiDefaults.OllamaProvider);
        local.Calls.Should().Be(1);
        claude.Calls.Should().Be(0);
    }

    [Fact]
    public async Task CompleteAsync_ProviderNameInOtherCase_StillDispatches()
    {
        var claude = new StubBackend(AiDefaults.ClaudeProvider);
        var router = new AiProviderRouter([claude]);

        await router.CompleteAsync(Request("claude"), CancellationToken.None);

        claude.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Ollama")]
    [InlineData("")]
    public async Task UnknownProvider_ThrowsWithoutCallingAnyBackend(string provider)
    {
        var claude = new StubBackend(AiDefaults.ClaudeProvider);
        var router = new AiProviderRouter([claude]);

        var json = () => router.CompleteJsonAsync<string>(Request(provider), CancellationToken.None);
        var text = () => router.CompleteAsync(Request(provider), CancellationToken.None);
        var tools = () => router.CompleteWithToolsAsync(
            new AiToolRequest { Provider = provider, Model = "m", Messages = [] }, CancellationToken.None);

        (await json.Should().ThrowAsync<UnknownAiProviderException>()).Which.Provider.Should().Be(provider);
        await text.Should().ThrowAsync<UnknownAiProviderException>();
        await tools.Should().ThrowAsync<UnknownAiProviderException>();
        claude.Calls.Should().Be(0, "no fallback to another provider: nothing is sent anywhere");
    }

    [Fact]
    public void Constructor_TwoBackendsWithTheSameName_Throws()
    {
        var act = () => new AiProviderRouter([new StubBackend("Claude"), new StubBackend("claude")]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddCofferAi_ResolvesTheRouterOverTheClaudeAndOllamaBackends()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddCofferAi();
        // Replaces the platform secret store (DPAPI needs vault paths) — only the registration shape is under test.
        services.AddSingleton<ISecretStore>(new InMemorySecretStore());
        services.AddSingleton<IAiSettings>(new StubAiSettings());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAiProvider>().Should().BeOfType<AiProviderRouter>();
        provider.GetServices<IAiProviderBackend>().Select(b => b.GetType()).Should()
            .BeEquivalentTo([typeof(ClaudeProvider), typeof(OllamaProvider)]);
        provider.GetRequiredService<IOllamaCatalog>().Should().BeOfType<OllamaCatalog>();
        provider.GetRequiredService<OllamaApiClient>().Should()
            .BeSameAs(provider.GetRequiredService<OllamaApiClient>(), "one HttpClient for the process");
        provider.GetRequiredService<IAiRouteReadiness>().Should().BeOfType<AiRouteReadiness>();
        provider.GetRequiredService<IAiRouteResolver>().Should().BeOfType<AiRouteResolver>();
    }

    private static AiRequest Request(string provider) =>
        new() { Prompt = "p", Provider = provider, Model = "m" };

    /// <summary>A backend that answers with its own provider name, so a test sees who served the call.</summary>
    private sealed class StubBackend(string name) : IAiProviderBackend
    {
        public string ProviderName => name;

        public int Calls { get; private set; }

        public Task<bool> IsConfiguredAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<AiResult<string>> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new AiResult<string>(name, Usage(request.Model)));
        }

        public Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new AiResult<TResult>((TResult)(object)name, Usage(request.Model)));
        }

        public Task<AiResult<AiToolTurn>> CompleteWithToolsAsync(AiToolRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new AiResult<AiToolTurn>(new AiToolTurn(name, []), Usage(request.Model)));
        }

        public IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        private AiUsage Usage(string model) => new(name, model, 1, 1);
    }
}
