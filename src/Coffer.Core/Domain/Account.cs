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

    /// <summary>
    /// Date the owner reconciled the real balance (<see cref="AnchorBalance"/>). The absolute
    /// balance on any later date is derived as anchor plus the sum of transactions after this
    /// date. Null until the owner sets an anchor; the balance then stays a relative running sum.
    /// </summary>
    public DateOnly? AnchorDate { get; set; }

    /// <summary>
    /// The real account balance as of <see cref="AnchorDate"/>, entered manually by the owner.
    /// Null when no anchor is set. Set together with <see cref="AnchorDate"/>.
    /// </summary>
    public decimal? AnchorBalance { get; set; }
}
