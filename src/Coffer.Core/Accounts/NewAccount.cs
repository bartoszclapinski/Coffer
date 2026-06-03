using Coffer.Core.Domain;

namespace Coffer.Core.Accounts;

/// <summary>
/// The fields needed to create an <see cref="Account"/> from the import screen's
/// inline "new account" form. The id and <c>CreatedAt</c> are assigned by the
/// service.
/// </summary>
public sealed record NewAccount(
    string Name,
    string BankCode,
    string AccountNumber,
    string Currency,
    AccountType Type);
