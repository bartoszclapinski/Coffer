namespace Coffer.Core.Goals;

/// <summary>
/// Models a one-off mortgage prepayment under both <see cref="PrepaymentMode"/>s (doc 07). Pure and
/// deterministic — the same loan terms always yield the same effect. The calculator never picks a
/// mode for the owner; it quantifies each so the UI can show both (avoids the "advisor that
/// recommends" pitfall).
/// </summary>
public interface IMortgagePrepaymentCalculator
{
    /// <summary>Quantifies a prepayment under a single mode.</summary>
    /// <param name="principalRemaining">Outstanding loan balance before the prepayment.</param>
    /// <param name="annualRate">Nominal annual interest rate (e.g. 0.072m for 7.2%).</param>
    /// <param name="monthsRemaining">Months left on the current schedule.</param>
    /// <param name="prepayment">The one-off extra principal payment.</param>
    /// <param name="mode">Whether to shorten the term or reduce the instalment.</param>
    PrepaymentEffect Calculate(
        decimal principalRemaining,
        decimal annualRate,
        int monthsRemaining,
        decimal prepayment,
        PrepaymentMode mode);
}
