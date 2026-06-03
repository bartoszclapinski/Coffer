namespace Coffer.Core.Transactions;

/// <summary>
/// A read-only projection of a transaction for the list view: the row's own fields
/// plus the resolved account name and (optional) category name/colour, so the UI
/// binds directly without loading full entities.
/// </summary>
public sealed record TransactionListItem(
    Guid Id,
    DateOnly Date,
    string Description,
    string? Merchant,
    decimal Amount,
    string Currency,
    Guid AccountId,
    string AccountName,
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryColor);
