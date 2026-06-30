using Coffer.Core.Domain;

namespace Coffer.Core.Planning;

/// <summary>
/// One dated occurrence of a <see cref="RecurringFlow"/> on the projection timeline.
/// <see cref="Amount"/> is signed (negative = outflow); <see cref="BalanceAfter"/> is the running
/// balance once this event is applied. <see cref="AccrualPeriod"/> is the first day of the month the
/// cost belongs to (the payment month shifted back by the flow's accrual offset).
/// <see cref="IsTight"/> marks that the balance fell to or below the projection's tight floor here.
/// </summary>
public sealed record CashFlowEvent(
    Guid FlowId,
    string Name,
    DateOnly Date,
    FlowDirection Direction,
    decimal Amount,
    DateOnly AccrualPeriod,
    decimal BalanceAfter,
    bool IsTight);
