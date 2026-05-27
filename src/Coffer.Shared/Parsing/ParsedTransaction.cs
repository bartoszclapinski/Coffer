namespace Coffer.Shared.Parsing;

/// <summary>
/// One transaction extracted from a bank statement, before persistence. Decimal
/// money per hard rule #1, <see cref="DateOnly"/> dates per hard rule #2, and
/// <see cref="Currency"/> is always populated per hard rule #9 (even when
/// 99% of statements are PLN-only — the schema supports future multi-currency
/// accounts).
/// </summary>
/// <param name="Date">Operation date — the day the user transacted.</param>
/// <param name="BookingDate">Optional booking date the bank recorded; null when the statement omits it.</param>
/// <param name="Amount">Signed: debits negative, credits positive.</param>
/// <param name="Currency">ISO 4217 code (e.g. "PLN", "EUR", "USD").</param>
/// <param name="Description">Raw description as printed on the statement.</param>
/// <param name="Merchant">Extracted merchant when the parser identifies one; null otherwise.</param>
public sealed record ParsedTransaction(
    DateOnly Date,
    DateOnly? BookingDate,
    decimal Amount,
    string Currency,
    string Description,
    string? Merchant);
