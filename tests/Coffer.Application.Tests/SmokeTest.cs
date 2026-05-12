using FluentAssertions;

namespace Coffer.Application.Tests;

public class SmokeTest
{
    [Fact]
    public void TestRunner_AndFluentAssertions_AreWiredUp()
    {
        true.Should().BeTrue();
    }
}
