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

    /// <summary>Creates a new account and returns its id.</summary>
    Task<Guid> CreateAsync(NewAccount account, CancellationToken ct);
}
