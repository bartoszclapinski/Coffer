namespace Coffer.Core.Goals;

/// <summary>
/// The two ways a one-off mortgage prepayment can be applied (doc 07). The engine shows both
/// outcomes and recommends neither — the owner chooses (avoids the "advisor that recommends" pitfall).
/// </summary>
public enum PrepaymentMode
{
    /// <summary>Keep the monthly payment; shorten the loan term.</summary>
    Shorten,

    /// <summary>Keep the term; reduce the monthly payment.</summary>
    Reduce,
}
