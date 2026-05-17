using Coffer.Infrastructure.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Security;

public class ZxcvbnPasswordStrengthCheckerTests
{
    [Fact]
    public void Evaluate_EmptyPassword_ReturnsZero()
    {
        var checker = new ZxcvbnPasswordStrengthChecker();

        var strength = checker.Evaluate("");

        strength.Score.Should().Be(0);
    }

    [Fact]
    public void Evaluate_WeakPassword_ScoresLow()
    {
        var checker = new ZxcvbnPasswordStrengthChecker();

        var strength = checker.Evaluate("password");

        strength.Score.Should().BeLessThan(3);
    }

    [Fact]
    public void Evaluate_StrongPassword_ScoresHigh()
    {
        var checker = new ZxcvbnPasswordStrengthChecker();

        var strength = checker.Evaluate("Tr0ub4dor&3-correct-horse-staple");

        strength.Score.Should().BeGreaterOrEqualTo(3);
    }
}
