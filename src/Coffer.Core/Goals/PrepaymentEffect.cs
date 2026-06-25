namespace Coffer.Core.Goals;

/// <summary>
/// The outcome of applying a one-off prepayment under a single <see cref="PrepaymentMode"/> (doc 07).
/// The engine produces one of these per mode and shows both side by side; it recommends neither.
/// All money is <c>decimal</c> (hard rule #1); the amortization is a month-by-month simulation, so
/// there is no floating-point drift.
/// </summary>
/// <param name="Mode">Which application was modelled.</param>
/// <param name="InterestSaved">Total interest avoided over the remaining loan versus making no prepayment.</param>
/// <param name="NewMonthlyPayment">
/// The monthly instalment after the prepayment. Unchanged from the original under
/// <see cref="PrepaymentMode.Shorten"/>; lower under <see cref="PrepaymentMode.Reduce"/>.
/// </param>
/// <param name="NewMonthsRemaining">
/// Months left after the prepayment. Fewer than the original under <see cref="PrepaymentMode.Shorten"/>;
/// unchanged under <see cref="PrepaymentMode.Reduce"/>.
/// </param>
/// <param name="MonthsSaved">Months removed from the schedule (zero under <see cref="PrepaymentMode.Reduce"/>).</param>
/// <param name="BreakEvenMonths">
/// Whole months until the cumulative interest saved covers the prepayment outlay — a rough
/// "when does this pay for itself" marker, not a recommendation.
/// </param>
public sealed record PrepaymentEffect(
    PrepaymentMode Mode,
    decimal InterestSaved,
    decimal NewMonthlyPayment,
    int NewMonthsRemaining,
    int MonthsSaved,
    int BreakEvenMonths);
