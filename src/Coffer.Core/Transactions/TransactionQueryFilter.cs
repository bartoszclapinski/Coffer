namespace Coffer.Core.Transactions;

/// <summary>
/// Filters for <see cref="IGetTransactionsQuery"/>. All members are optional: a
/// null <see cref="From"/> defaults to a six-month window ending today; the rest
/// are AND-combined when set. <see cref="Search"/> matches the description or
/// merchant (case-insensitive substring).
/// </summary>
public sealed record TransactionQueryFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    string? Search = null,
    Guid? AccountId = null,
    Guid? CategoryId = null);
