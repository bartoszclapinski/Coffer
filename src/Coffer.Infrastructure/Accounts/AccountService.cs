using Coffer.Core.Accounts;
using Coffer.Core.Domain;
using Coffer.Core.Transactions;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Accounts;

/// <summary>
/// EF-backed <see cref="IAccountService"/>. Lists non-archived accounts for the
/// picker and persists inline-created ones.
/// </summary>
public sealed class AccountService : IAccountService
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public AccountService(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<AccountListItem>> GetAllAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db.Accounts.AsNoTracking()
            .Where(a => !a.IsArchived)
            .OrderBy(a => a.Name)
            .Select(a => new AccountListItem(a.Id, a.Name, a.BankCode))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountAnchorItem>> GetAllWithAnchorsAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db.Accounts.AsNoTracking()
            .Where(a => !a.IsArchived)
            .OrderBy(a => a.Name)
            .Select(a => new AccountAnchorItem(a.Id, a.Name, a.BankCode, a.AnchorDate, a.AnchorBalance))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Guid> CreateAsync(NewAccount account, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(account);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var entity = new Account
        {
            Id = Guid.NewGuid(),
            Name = account.Name,
            BankCode = account.BankCode,
            AccountNumber = account.AccountNumber,
            Currency = account.Currency,
            Type = account.Type,
            CreatedAt = DateTime.UtcNow,
        };

        db.Accounts.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task SetBalanceAnchorAsync(Guid accountId, decimal? balance, DateOnly? date, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Load tracked (no AsNoTracking) so the assignment is persisted on SaveChanges.
        var account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Account {accountId} was not found.");

        account.AnchorBalance = balance;
        account.AnchorDate = date;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
