using Coffer.Core.Transactions;

namespace Coffer.Core.Accounts;

/// <summary>
/// Lists and creates accounts for the import screen. The implementation lives in
/// <c>Coffer.Infrastructure</c> (it writes through the EF context); the
/// <c>Coffer.Application</c> import view model depends on this abstraction so the
/// user can pick an existing account or create one inline before importing.
/// </summary>
public interface IAccountService
{
    /// <summary>Non-archived accounts ordered by name, for the account picker.</summary>
    Task<IReadOnlyList<AccountListItem>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// Non-archived accounts with their balance anchor (18-A), ordered by name, for the Settings anchor
    /// editor and the affordability account selector.
    /// </summary>
    Task<IReadOnlyList<AccountAnchorItem>> GetAllWithAnchorsAsync(CancellationToken ct);

    /// <summary>Creates a new account and returns its id.</summary>
    Task<Guid> CreateAsync(NewAccount account, CancellationToken ct);

    /// <summary>
    /// Sets or clears an account's balance anchor (18-A). Both parameters are set together: pass the
    /// owner's real balance and the date it was true to anchor the account, or both <c>null</c> to clear
    /// it (the balance reverts to a relative running sum). Throws if the account does not exist.
    /// </summary>
    Task SetBalanceAnchorAsync(Guid accountId, decimal? balance, DateOnly? date, CancellationToken ct);
}
