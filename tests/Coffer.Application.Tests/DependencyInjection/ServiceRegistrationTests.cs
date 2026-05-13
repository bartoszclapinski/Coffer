using Coffer.Application.DependencyInjection;
using Coffer.Core.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Coffer.Application.Tests.DependencyInjection;

public class ServiceRegistrationTests
{
    [Fact]
    public void AddCofferCore_ReturnsSameCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddCofferCore();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddCofferApplication_ReturnsSameCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddCofferApplication();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddCofferCore_ThenAddCofferApplication_BuildsServiceProviderWithoutThrowing()
    {
        var services = new ServiceCollection()
            .AddCofferCore()
            .AddCofferApplication();

        var act = () => services.BuildServiceProvider(validateScopes: true);

        act.Should().NotThrow();
    }
}
