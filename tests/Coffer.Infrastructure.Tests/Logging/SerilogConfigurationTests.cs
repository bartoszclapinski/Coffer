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
    public void AddCofferLogging_AllowsGenericLoggerResolution()
    {
        var services = new ServiceCollection().AddCofferLogging();

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<SerilogConfigurationTests>>();

        logger.Should().NotBeNull();
    }
}
