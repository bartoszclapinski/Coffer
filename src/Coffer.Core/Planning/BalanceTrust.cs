namespace Coffer.Core.Planning;

/// <summary>
/// Whether the derived balance for an account is trustworthy as of a date. <see cref="IsTrustworthy"/>
/// is true only when no statement gap falls inside the dependency window
/// (<see cref="WindowFrom"/>..asOf). <see cref="Gaps"/> lists the offending gaps so the UI/assistant can
/// name the missing days.
/// </summary>
public sealed record BalanceTrust(bool IsTrustworthy, DateOnly WindowFrom, IReadOnlyList<StatementGap> Gaps);
