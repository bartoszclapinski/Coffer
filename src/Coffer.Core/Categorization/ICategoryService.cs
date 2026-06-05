namespace Coffer.Core.Categorization;

/// <summary>
/// UI-facing categorisation operations that <c>Coffer.Application</c> view models call
/// (the implementation in <c>Coffer.Infrastructure</c> owns the DB context). Covers the
/// category list for pickers, manual re-categorisation (which feeds the learning loop),
/// and a one-shot pass over already-imported uncategorised rows.
/// </summary>
public interface ICategoryService
{
    /// <summary>Non-archived categories for the picker / filter, ordered by name.</summary>
    Task<IReadOnlyList<CategoryListItem>> GetCategoriesAsync(CancellationToken ct);

    /// <summary>
    /// Assigns <paramref name="categoryId"/> to a transaction by hand and records the
    /// choice as a <c>Manual</c> cache entry, so the same description is categorised the
    /// same way on the next import. Returns the transaction's normalised description so the
    /// caller can update sibling rows sharing it, or null if the transaction is gone.
    /// </summary>
    Task<string?> SetCategoryAsync(Guid transactionId, Guid categoryId, CancellationToken ct);

    /// <summary>
    /// Runs the deterministic categoriser over every uncategorised transaction and persists
    /// the resolved categories. Returns the number of transactions newly categorised.
    /// </summary>
    Task<int> RecategorizeUncategorizedAsync(CancellationToken ct);
}
