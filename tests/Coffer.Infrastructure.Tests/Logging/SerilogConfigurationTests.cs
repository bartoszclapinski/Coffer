using Coffer.Infrastructure.Logging;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Tests.Logging;

public class SerilogConfigurationTests
{
    [Fact]
    public void AddCofferLogging_RegistersLoggerFactory()
    {
        var services = new ServiceCollection().AddCofferLogging();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ILoggerFactory>();

        factory.Should().NotBeNull();
    }

    [Fact]
    public void AddCofferLogging_WithDataRoot_CreatesTheLogFolderUnderThatRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Coffer.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection().AddCofferLogging(root);
            using var provider = services.BuildServiceProvider();

            Directory.Exists(Path.Combine(root, "logs")).Should().BeTrue();
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort — a log file may still be open by the global Serilog logger.
            }
        }
    }

    [Fact]
    public void AddCofferLogging_AllowsGenericLoggerResolution()
    {
        var services = new ServiceCollection().AddCofferLogging();

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<SerilogConfigurationTests>>();

        logger.Should().NotBeNull();
    }
}
