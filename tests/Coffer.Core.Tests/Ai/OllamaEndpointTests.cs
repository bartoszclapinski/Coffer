using Coffer.Core.Ai;
using FluentAssertions;

namespace Coffer.Core.Tests.Ai;

public class OllamaEndpointTests
{
    [Theory]
    [InlineData("http://localhost:11434", "http://localhost:11434")]
    [InlineData("  http://localhost:11434/  ", "http://localhost:11434")]
    [InlineData("https://ollama.example.test", "https://ollama.example.test")]
    [InlineData("http://10.0.0.5:11434/ollama/", "http://10.0.0.5:11434/ollama")]
    public void TryParse_ValidEndpoint_ReturnsCanonicalForm(string text, string expected)
    {
        OllamaEndpoint.TryParse(text, out var endpoint).Should().BeTrue();

        OllamaEndpoint.Format(endpoint!).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost:11434")]
    [InlineData("/api/chat")]
    [InlineData("ftp://localhost:11434")]
    [InlineData("file:///c:/ollama")]
    [InlineData("http://user:secret@localhost:11434")]
    [InlineData("http://localhost:11434/?token=x")]
    [InlineData("http://localhost:11434/#frag")]
    public void TryParse_InvalidEndpoint_ReturnsFalse(string? text)
    {
        OllamaEndpoint.TryParse(text, out var endpoint).Should().BeFalse();

        endpoint.Should().BeNull();
    }

    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://LOCALHOST:11434", true)]
    [InlineData("http://127.0.0.1:11434", true)]
    [InlineData("http://127.0.0.2:11434", true)]
    [InlineData("http://[::1]:11434", true)]
    [InlineData("http://192.168.1.20:11434", false)]
    [InlineData("http://0.0.0.0:11434", false)]
    [InlineData("http://localhost.example.test:11434", false)]
    [InlineData("https://ollama.example.test", false)]
    public void IsLoopback_ReportsWhetherTheHostIsThisMachine(string endpoint, bool expected)
    {
        OllamaEndpoint.IsLoopback(new Uri(endpoint)).Should().Be(expected);
    }

    [Theory]
    [InlineData("http://localhost:11434", "http://localhost:11434/api/chat")]
    [InlineData("http://localhost:11434/", "http://localhost:11434/api/chat")]
    [InlineData("http://proxy.test/ollama", "http://proxy.test/ollama/api/chat")]
    public void Combine_AppendsTheApiPathUnderAnyPrefix(string endpoint, string expected)
    {
        OllamaEndpoint.Combine(new Uri(endpoint), "api/chat").Should().Be(new Uri(expected));
    }

    [Fact]
    public void Default_IsTheLocalOllamaPort()
    {
        OllamaEndpoint.Default.Should().Be(new Uri(AiDefaults.OllamaEndpoint));
        OllamaEndpoint.IsLoopback(OllamaEndpoint.Default).Should().BeTrue();
    }
}
