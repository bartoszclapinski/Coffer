namespace Coffer.Core.Transactions;

/// <summary>
/// A lightweight account projection for the filter dropdown.
/// </summary>
public sealed record AccountListItem(Guid Id, string Name, string BankCode);
