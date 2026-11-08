using System.Net;
using Coffer.Core.Ai;
using Coffer.Infrastructure.AI.Ollama;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI.Ollama;

public class OllamaCatalogTests
{
    private static readonly Uri _endpoint = new("http://localhost:11434");

    [Fact]
    public async Task TestConnection_Reachable_ReturnsTheVersion()
    {
        var handler = new FakeOllamaHandler().Respond("""{"version":"0.40.15"}""");

        var result = await Catalog(handler).TestConnectionAsync(_endpoint, CancellationToken.None);

        result.Should().Be(OllamaConnectionResult.Connected("0.40.15"));
        result.IsSuccess.Should().BeTrue();
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Uri.Should().Be(new Uri("http://localhost:11434/api/version"));
    }

    [Fact]
    public async Task TestConnection_Refused_ReturnsUnreachableWithoutThrowing()
    {
        var handler = new FakeOllamaHandler().Throw(new HttpRequestException("refused"));

        var result = await Catalog(handler).TestConnectionAsync(_endpoint, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Failure.Should().Be(AiProviderUnavailableReason.Unreachable);
        result.Version.Should().BeNull();
    }

    [Fact]
    public async Task TestConnection_JsonWithoutVersion_IsNotOllama()
    {
        var handler = new FakeOllamaHandler().Respond("""{"status":"ok"}""");

        var result = await Catalog(handler).TestConnectionAsync(_endpoint, CancellationToken.None);

        result.Failure.Should().Be(AiProviderUnavailableReason.Rejected);
    }

    [Fact]
    public async Task TestConnection_NoAnswer_TimesOutWithTheShortCatalogTimeout()
    {
        var handler = new FakeOllamaHandler().Hang();
        var catalog = new OllamaCatalog(new OllamaApiClient(handler, catalogTimeout: TimeSpan.FromMilliseconds(50)));

        var result = await catalog.TestConnectionAsync(_endpoint, CancellationToken.None);

        result.Failure.Should().Be(AiProviderUnavailableReason.Timeout);
    }

    [Fact]
    public async Task TestConnection_CallerCancellation_Propagates()
    {
        var handler = new FakeOllamaHandler().Hang();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => Catalog(handler).TestConnectionAsync(_endpoint, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ListModels_ParsesNamesAndCapabilitiesSortedByName()
    {
        var handler = new FakeOllamaHandler().Respond(
            """
            {"models":[
              {"name":"qwen3.5:latest","model":"qwen3.5:latest","size":6594474711,
               "details":{"family":"qwen35","parameter_size":"9.7B"},
               "capabilities":["completion","vision","tools","thinking"]},
              {"name":"llama3.2:1b","model":"llama3.2:1b"},
              {"name":"qwen2.5:3b","model":"qwen2.5:3b","capabilities":["completion","tools"]},
              {"model":"only-model-field:7b","capabilities":["completion"]},
              {"size":1}
            ]}
            """);

        var models = await Catalog(handler).ListModelsAsync(new Uri("http://proxy.test/ollama"), CancellationToken.None);

        handler.Requests.Single().Uri.Should().Be(new Uri("http://proxy.test/ollama/api/tags"));
        models.Select(m => m.Name).Should().Equal("llama3.2:1b", "only-model-field:7b", "qwen2.5:3b", "qwen3.5:latest");
        models[2].Capabilities.Should().Equal("completion", "tools");
        models[3].Capabilities.Should().Contain("thinking");
        models[0].Capabilities.Should().BeEmpty();
        models[0].SupportsTools.Should().BeTrue("an older server that reports no capabilities is not ruled out");
        models[1].SupportsTools.Should().BeFalse();
    }

    [Fact]
    public async Task ListModels_NoModelsArray_ReturnsEmpty()
    {
        var handler = new FakeOllamaHandler().Respond("""{"models":null}""");

        var models = await Catalog(handler).ListModelsAsync(_endpoint, CancellationToken.None);

        models.Should().BeEmpty();
    }

    [Fact]
    public async Task ListModels_ServerError_ThrowsUnavailableWithoutAModel()
    {
        var handler = new FakeOllamaHandler().Respond("""{"error":"boom"}""", HttpStatusCode.InternalServerError);

        var act = () => Catalog(handler).ListModelsAsync(_endpoint, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<AiProviderUnavailableException>()).Which;
        ex.Reason.Should().Be(AiProviderUnavailableReason.Rejected);
        ex.Model.Should().BeNull();
        ex.Endpoint.Should().Be("http://localhost:11434");
    }

    [Fact]
    public async Task ListModels_NotFound_IsRejectedNotModelNotFound()
    {
        // A 404 on /api/tags means the endpoint is not an Ollama (or a wrong path prefix), not a missing model.
        var handler = new FakeOllamaHandler().Respond("404 page not found", HttpStatusCode.NotFound);

        var act = () => Catalog(handler).ListModelsAsync(_endpoint, CancellationToken.None);

        (await act.Should().ThrowAsync<AiProviderUnavailableException>())
            .Which.Reason.Should().Be(AiProviderUnavailableReason.Rejected);
    }

    private static OllamaCatalog Catalog(FakeOllamaHandler handler) => new(new OllamaApiClient(handler));
}
