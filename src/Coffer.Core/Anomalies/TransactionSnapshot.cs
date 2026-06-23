namespace Coffer.Core.Anomalies;

/// <summary>
/// The minimal projection of a transaction the detectors reason over. Keeping detectors
/// off the EF entity makes them pure and trivially unit-testable with plain records.
/// <see cref="Amount"/> is signed (negative = debit) and always <c>decimal</c> (hard rule #1).
/// </summary>
public sealed record TransactionSnapshot(
    Guid Id,
    DateOnly Date,
    DateOnly? BookingDate,
    decimal Amount,
    string? Merchant,
    string NormalizedDescription,
    Guid? CategoryId);
