using Coffer.Core.Transactions;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Transactions;

/// <summary>
/// Read-side query over <c>Transactions</c> for the list view. Runs untracked,
/// filters and orders server-side, and projects directly into
/// <see cref="TransactionListItem"/> so no entities are materialised.
/// </summary>
public sealed class GetTransactionsQuery : IGetTransactionsQuery
{
    private const int DefaultWindowMonths = 6;

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public GetTransactionsQuery(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<TransactionListItem>> ExecuteAsync(
        TransactionQueryFilter filter,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var from = filter.From ?? DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-DefaultWindowMonths);

        var query = db.Transactions.AsNoTracking().Where(t => t.Date >= from);

        if (filter.To is { } to)
        {
            query = query.Where(t => t.Date <= to);
        }

        if (filter.AccountId is { } accountId)
        {
            query = query.Where(t => t.AccountId == accountId);
        }

        if (filter.CategoryId is { } categoryId)
        {
            query = query.Where(t => t.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(t =>
                t.Description.Contains(search) ||
                (t.Merchant != null && t.Merchant.Contains(search)));
        }

        return await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t => new TransactionListItem(
                t.Id,
                t.Date,
                t.Description,
                t.Merchant,
                t.Amount,
                t.Currency,
                t.AccountId,
                t.Account.Name,
                t.CategoryId,
                t.Category != null ? t.Category.Name : null,
                t.Category != null ? t.Category.Color : null))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountListItem>> GetAccountsAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db.Accounts.AsNoTracking()
            .Where(a => !a.IsArchived)
            .OrderBy(a => a.Name)
            .Select(a => new AccountListItem(a.Id, a.Name, a.BankCode))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
