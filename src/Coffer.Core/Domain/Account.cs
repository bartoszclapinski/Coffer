namespace Coffer.Core.Domain;

/// <summary>
/// A bank account a user imports statements into. The account number is normalised
/// (digits with a country prefix); PKO "Historia rachunku" CSV omits it, so the user
/// confirms the account at import time rather than relying on the statement.
/// </summary>
public class Account
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string BankCode { get; set; } = "";

    public string AccountNumber { get; set; } = "";

    public string Currency { get; set; } = "PLN";

    public AccountType Type { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; }
}
