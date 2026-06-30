using Coffer.Core.Domain;

namespace Coffer.Core.Planning;

/// <summary>
/// A recurring flow proposed by <see cref="IRecurringFlowDetector"/> from transaction history, before
/// the owner confirms it as a persisted <see cref="RecurringFlow"/>. Carries the inferred cadence and
/// amount statistics; the accrual offset is left to the owner (it is not derivable from bank data).
/// </summary>
public sealed record RecurringFlowCandidate(
    string Name,
    FlowDirection Direction,
    string? MatchMerchant,
    Guid? MatchCategoryId,
    int IntervalMonths,
    int AnchorDayOfMonth,
    int? AnchorMonth,
    decimal TypicalAmount,
    decimal AmountStdDev,
    int MonthsObserved);
