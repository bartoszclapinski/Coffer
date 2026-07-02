namespace Coffer.Core.Accounts;

/// <summary>
/// An account projection carrying its balance anchor (18-A), for the Settings anchor editor. When
/// <see cref="AnchorDate"/>/<see cref="AnchorBalance"/> are null the account has no reconciled anchor and
/// its balance is a relative running sum; when set they are the owner's "real balance was X on date Y".
/// </summary>
public sealed record AccountAnchorItem(
    Guid Id,
    string Name,
    string BankCode,
    DateOnly? AnchorDate,
    decimal? AnchorBalance);
