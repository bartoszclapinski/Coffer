using Coffer.Core.Goals;
using Coffer.Infrastructure.Goals;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Goals;

public class MortgagePrepaymentCalculatorTests
{
    private readonly MortgagePrepaymentCalculator _calc = new();

    [Fact]
    public void Calculate_AnnuityPayment_MatchesHandComputedValue()
    {
        // P=1000, 12% annual (1% monthly), 2 months. Annuity = P*r*(1+r)^2/((1+r)^2-1)
        // = 1000*0.01*1.0201/0.0201 ≈ 507.51. A zero prepayment leaves the payment unchanged.
        var effect = _calc.Calculate(1000m, 0.12m, 2, 0m, PrepaymentMode.Reduce);

        effect.NewMonthlyPayment.Should().BeApproximately(507.51m, 0.05m);
        effect.InterestSaved.Should().Be(0m);
        effect.NewMonthsRemaining.Should().Be(2);
    }

    [Fact]
    public void Calculate_Shorten_KeepsPaymentAndCutsTerm()
    {
        var baseline = _calc.Calculate(300_000m, 0.072m, 240, 0m, PrepaymentMode.Shorten);
        var effect = _calc.Calculate(300_000m, 0.072m, 240, 50_000m, PrepaymentMode.Shorten);

        effect.NewMonthlyPayment.Should().BeApproximately(baseline.NewMonthlyPayment, 0.01m);
        effect.NewMonthsRemaining.Should().BeLessThan(240);
        effect.MonthsSaved.Should().BeGreaterThan(0);
        effect.InterestSaved.Should().BeGreaterThan(0m);
        effect.BreakEvenMonths.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Calculate_Reduce_KeepsTermAndLowersPayment()
    {
        var baseline = _calc.Calculate(300_000m, 0.072m, 240, 0m, PrepaymentMode.Reduce);
        var effect = _calc.Calculate(300_000m, 0.072m, 240, 50_000m, PrepaymentMode.Reduce);

        effect.NewMonthsRemaining.Should().Be(240);
        effect.MonthsSaved.Should().Be(0);
        effect.NewMonthlyPayment.Should().BeLessThan(baseline.NewMonthlyPayment);
        effect.InterestSaved.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void Calculate_PrepaymentClearsLoan_SavesAllInterestAndEndsLoan()
    {
        var effect = _calc.Calculate(100_000m, 0.06m, 120, 100_000m, PrepaymentMode.Shorten);

        effect.NewMonthsRemaining.Should().Be(0);
        effect.NewMonthlyPayment.Should().Be(0m);
        effect.MonthsSaved.Should().Be(120);
        // Clearing the loan outright avoids every remaining złoty of interest.
        effect.InterestSaved.Should().BeGreaterThan(20_000m);
    }

    [Fact]
    public void Calculate_ZeroRate_IsExactAndDriftFree()
    {
        // No interest: 120000 over 120 months ⇒ 1000/mo. A 12000 prepayment removes 12 instalments.
        var effect = _calc.Calculate(120_000m, 0m, 120, 12_000m, PrepaymentMode.Shorten);

        effect.NewMonthlyPayment.Should().Be(1000m);
        effect.MonthsSaved.Should().Be(12);
        effect.NewMonthsRemaining.Should().Be(108);
        effect.InterestSaved.Should().Be(0m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Calculate_NoLoan_ReturnsEmptyEffect(int monthsRemaining)
    {
        var effect = _calc.Calculate(0m, 0.05m, monthsRemaining, 1000m, PrepaymentMode.Reduce);

        effect.InterestSaved.Should().Be(0m);
        effect.NewMonthsRemaining.Should().Be(0);
    }
}
