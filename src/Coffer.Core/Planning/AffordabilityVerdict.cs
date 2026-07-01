namespace Coffer.Core.Planning;

/// <summary>
/// The recurring flow that drives the projected low point — "what pushes you under" — when the low is
/// caused by a dated payment rather than pure day-to-day burn. <see cref="Amount"/> is signed
/// (negative = outflow), mirroring <see cref="CashFlowEvent.Amount"/>. Null on a verdict means the low
/// point came from the proposed spend plus ordinary variable burn, not from any single recurring flow.
/// </summary>
public sealed record AffordabilityDriver(Guid FlowId, string Name, DateOnly Date, decimal Amount);

/// <summary>
/// The deterministic answer to "can I spend <see cref="SpendAmount"/> on <see cref="SpendDate"/>?"
/// produced by <see cref="AffordabilityEngine"/>. Every figure is computed in C# — the assistant and the
/// UI only narrate it (the Sprint-14 "engine calculates, AI explains" rule). All money is
/// <c>decimal</c> (rule #1); all dates are <see cref="DateOnly"/> (rule #2).
///
/// <para><see cref="CanAfford"/> is <c>true</c> when the lowest projected balance between the spend and
/// the next inflow stays at or above the owner's <see cref="SafetyFloor"/>. <see cref="Headroom"/> is
/// how far the low point clears the floor (negative = it breaches it). <see cref="IsUncertain"/> flags
/// that a statement gap sits in the window the opening balance depends on, so the number is not to be
/// trusted; <see cref="IsRelative"/> flags that the account has no balance anchor, so the level is a
/// relative running sum rather than an absolute reconciled balance.</para>
/// </summary>
public sealed record AffordabilityVerdict(
    bool CanAfford,
    decimal SpendAmount,
    DateOnly SpendDate,
    decimal OpeningBalance,
    decimal LowestBalance,
    DateOnly LowestBalanceDate,
    decimal SafetyFloor,
    decimal Headroom,
    decimal DailyBurn,
    DateOnly? NextInflowDate,
    AffordabilityDriver? Driver,
    bool IsUncertain,
    StatementGap? UncertaintyGap,
    bool IsRelative);
