using Coffer.Core.Goals;

namespace Coffer.Infrastructure.Goals;

/// <summary>
/// Models a one-off mortgage prepayment by simulating the amortization schedule month by month in
/// <c>decimal</c> (hard rule #1) — no <see cref="Math.Pow"/>, so there is no floating-point drift on
/// money. The annuity instalment is derived from an integer-exponent decimal power, then both the
/// original and post-prepayment schedules are walked to compare interest paid. The calculator never
/// chooses a mode for the owner (doc 07): it quantifies each so the UI can show both.
/// </summary>
public sealed class MortgagePrepaymentCalculator : IMortgagePrepaymentCalculator
{
    private const int MaxMonths = 1200;

    public PrepaymentEffect Calculate(
        decimal principalRemaining,
        decimal annualRate,
        int monthsRemaining,
        decimal prepayment,
        PrepaymentMode mode)
    {
        if (principalRemaining <= 0m || monthsRemaining <= 0)
        {
            return new PrepaymentEffect(mode, 0m, 0m, 0, 0, 0);
        }

        var prepay = Clamp(prepayment, 0m, principalRemaining);
        var monthlyRate = annualRate / 12m;
        var originalPayment = AnnuityPayment(principalRemaining, monthlyRate, monthsRemaining);

        var original = Simulate(principalRemaining, originalPayment, monthlyRate);
        var balanceAfterPrepay = principalRemaining - prepay;

        if (balanceAfterPrepay <= 0m)
        {
            // The prepayment clears the loan outright: all remaining interest is saved.
            return new PrepaymentEffect(
                mode,
                InterestSaved: original.Interest,
                NewMonthlyPayment: 0m,
                NewMonthsRemaining: 0,
                MonthsSaved: original.Months,
                BreakEvenMonths: BreakEven(prepay, original));
        }

        var result = mode == PrepaymentMode.Shorten
            ? ShortenEffect(original, balanceAfterPrepay, originalPayment, monthlyRate, prepay)
            : ReduceEffect(original, balanceAfterPrepay, monthlyRate, monthsRemaining, prepay);

        return result;
    }

    private static PrepaymentEffect ShortenEffect(
        Schedule original,
        decimal balanceAfterPrepay,
        decimal originalPayment,
        decimal monthlyRate,
        decimal prepay)
    {
        var shortened = Simulate(balanceAfterPrepay, originalPayment, monthlyRate);
        return new PrepaymentEffect(
            PrepaymentMode.Shorten,
            InterestSaved: Math.Max(0m, original.Interest - shortened.Interest),
            NewMonthlyPayment: originalPayment,
            NewMonthsRemaining: shortened.Months,
            MonthsSaved: Math.Max(0, original.Months - shortened.Months),
            BreakEvenMonths: BreakEven(prepay, original));
    }

    private static PrepaymentEffect ReduceEffect(
        Schedule original,
        decimal balanceAfterPrepay,
        decimal monthlyRate,
        int monthsRemaining,
        decimal prepay)
    {
        var reducedPayment = AnnuityPayment(balanceAfterPrepay, monthlyRate, monthsRemaining);
        var reduced = Simulate(balanceAfterPrepay, reducedPayment, monthlyRate);
        return new PrepaymentEffect(
            PrepaymentMode.Reduce,
            InterestSaved: Math.Max(0m, original.Interest - reduced.Interest),
            NewMonthlyPayment: reducedPayment,
            NewMonthsRemaining: monthsRemaining,
            MonthsSaved: 0,
            BreakEvenMonths: BreakEven(prepay, original));
    }

    /// <summary>Walks the schedule to payoff, accumulating interest. The final instalment may be smaller.</summary>
    private static Schedule Simulate(decimal balance, decimal payment, decimal monthlyRate)
    {
        var interest = 0m;
        var months = 0;

        while (balance > 0m && months < MaxMonths)
        {
            var monthInterest = balance * monthlyRate;
            interest += monthInterest;
            balance = balance + monthInterest - payment;
            months++;

            // A non-amortizing instalment would loop forever; the annuity guarantees progress, but
            // guard anyway so a degenerate input can never hang the engine.
            if (payment <= monthInterest)
            {
                break;
            }
        }

        return new Schedule(Math.Max(0m, interest), months);
    }

    /// <summary>
    /// The level monthly instalment that amortizes <paramref name="principal"/> over
    /// <paramref name="months"/> at <paramref name="monthlyRate"/>, computed with a decimal power.
    /// </summary>
    private static decimal AnnuityPayment(decimal principal, decimal monthlyRate, int months)
    {
        if (months <= 0)
        {
            return principal;
        }

        if (monthlyRate <= 0m)
        {
            return principal / months;
        }

        var growth = PowDecimal(1m + monthlyRate, months);
        return principal * monthlyRate * growth / (growth - 1m);
    }

    /// <summary>Integer-exponent power in <c>decimal</c> by repeated multiplication (no <see cref="Math.Pow"/>).</summary>
    private static decimal PowDecimal(decimal value, int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= value;
        }

        return result;
    }

    /// <summary>
    /// A rough payback marker: how many months of the original loan's average interest the prepayment
    /// offsets. Not a recommendation, just a "when does this roughly pay for itself" hint.
    /// </summary>
    private static int BreakEven(decimal prepay, Schedule original)
    {
        if (prepay <= 0m || original.Months <= 0 || original.Interest <= 0m)
        {
            return 0;
        }

        var avgMonthlyInterest = original.Interest / original.Months;
        return (int)Math.Ceiling(prepay / avgMonthlyInterest);
    }

    private static decimal Clamp(decimal value, decimal min, decimal max) =>
        value < min ? min : value > max ? max : value;

    private readonly record struct Schedule(decimal Interest, int Months);
}
