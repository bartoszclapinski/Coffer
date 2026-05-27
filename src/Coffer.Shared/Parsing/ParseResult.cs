namespace Coffer.Shared.Parsing;

/// <summary>
/// Structured output of <c>IStatementParser.ParseAsync</c>. Consumed by the
/// Sprint-8 import flow which maps it onto <c>Transaction</c> entities and dedupes
/// against existing rows via <c>TransactionHash</c>.
/// </summary>
/// <param name="BankCode">Matches the parser's <c>BankCode</c>.</param>
/// <param name="AccountNumber">Normalised account number (IBAN-format, no spaces).</param>
/// <param name="Currency">ISO 4217 currency of the account this statement covers.</param>
/// <param name="PeriodFrom">First day covered by the statement.</param>
/// <param name="PeriodTo">Last day covered by the statement.</param>
/// <param name="Transactions">Transactions in statement order.</param>
/// <param name="Confidence">How much the parser trusts its own output.</param>
/// <param name="Warnings">Non-fatal issues the parser surfaced — for instance, a transaction with a missing booking date in a layout that usually carries one.</param>
public sealed record ParseResult(
    string BankCode,
    string AccountNumber,
    string Currency,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    IReadOnlyList<ParsedTransaction> Transactions,
    ParserConfidence Confidence,
    IReadOnlyList<string> Warnings);
