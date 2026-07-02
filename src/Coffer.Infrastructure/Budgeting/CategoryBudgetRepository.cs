using Coffer.Core.Budgeting;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Budgeting;

/// <summary>
/// EF-backed CRUD for <see cref="CategoryBudget"/> limits. Reads project through <c>AsNoTracking</c>;
/// <see cref="SetBudgetAsync"/> upserts the single active budget per (category, currency) on a tracked
/// entity. Deletes are hard removals (there is no budget history in v1).
/// </summary>
public sealed class CategoryBudgetRepository : ICategoryBudgetRepository
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public CategoryBudgetRepository(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<CategoryBudgetItem>> GetActiveAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var rows = await db.CategoryBudgets.AsNoTracking()
            .Where(b => b.IsActive)
            .Join(
                db.Categories,
                b => b.CategoryId,
                c => c.Id,
                (b, c) => new { b.Id, b.CategoryId, c.Name, c.Color, b.LimitAmount, b.Currency })
            .OrderBy(x => x.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .Select(x => new CategoryBudgetItem(x.Id, x.CategoryId, x.Name, x.Color, x.LimitAmount, x.Currency))
            .ToList();
    }

    public async Task SetBudgetAsync(Guid categoryId, decimal limitAmount, string currency, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var categoryExists = await db.Categories.AnyAsync(c => c.Id == categoryId, ct).ConfigureAwait(false);
        if (!categoryExists)
        {
            throw new InvalidOperationException($"Category {categoryId} does not exist.");
        }

        var existing = await db.CategoryBudgets
            .FirstOrDefaultAsync(b => b.IsActive && b.CategoryId == categoryId && b.Currency == currency, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.LimitAmount = limitAmount;
        }
        else
        {
            db.CategoryBudgets.Add(new CategoryBudget
            {
                Id = Guid.NewGuid(),
                CategoryId = categoryId,
                LimitAmount = limitAmount,
                Currency = currency,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid categoryId, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var budgets = await db.CategoryBudgets
            .Where(b => b.CategoryId == categoryId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (budgets.Count == 0)
        {
            return;
        }

        db.CategoryBudgets.RemoveRange(budgets);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
