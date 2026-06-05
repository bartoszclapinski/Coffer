using Coffer.Core.Categorization;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Categorization;

/// <summary>
/// UI-facing categorisation operations (<see cref="ICategoryService"/>): the picker list,
/// manual re-categorisation that feeds the learning loop, and a one-shot pass over
/// already-imported uncategorised rows. View models in <c>Coffer.Application</c> depend on
/// the Core abstraction; this implementation owns the DB context.
/// </summary>
public sealed class CategoryService : ICategoryService
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;
    private readonly ICategorizer _categorizer;
    private readonly ICategoryCacheStore _cacheStore;

    public CategoryService(
        IDbContextFactory<CofferDbContext> contextFactory,
        ICategorizer categorizer,
        ICategoryCacheStore cacheStore)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(categorizer);
        ArgumentNullException.ThrowIfNull(cacheStore);

        _contextFactory = contextFactory;
        _categorizer = categorizer;
        _cacheStore = cacheStore;
    }

    public async Task<IReadOnlyList<CategoryListItem>> GetCategoriesAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Categories.AsNoTracking()
            .Where(c => !c.IsArchived)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryListItem(c.Id, c.Name, c.Color))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<string?> SetCategoryAsync(Guid transactionId, Guid categoryId, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var transaction = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            .ConfigureAwait(false);
        if (transaction is null)
        {
            return null;
        }

        transaction.CategoryId = categoryId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The learning loop: a hand correction wins over any rule/AI guess for this
        // description on every future import.
        await _cacheStore
            .UpsertAsync(transaction.NormalizedDescription, categoryId, CacheSource.Manual, ct)
            .ConfigureAwait(false);

        return transaction.NormalizedDescription;
    }

    public async Task<int> RecategorizeUncategorizedAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var uncategorized = await db.Transactions
            .Where(t => t.CategoryId == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (uncategorized.Count == 0)
        {
            return 0;
        }

        var descriptions = uncategorized
            .Select(t => t.NormalizedDescription)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var resolved = await _categorizer.CategorizeAsync(descriptions, ct).ConfigureAwait(false);

        var changed = 0;
        foreach (var transaction in uncategorized)
        {
            if (resolved.TryGetValue(transaction.NormalizedDescription, out var categoryId)
                && categoryId is { } id)
            {
                transaction.CategoryId = id;
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return changed;
    }
}
