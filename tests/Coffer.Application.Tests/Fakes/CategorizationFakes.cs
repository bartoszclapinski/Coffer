using Coffer.Core.Categorization;

namespace Coffer.Application.Tests.Fakes;

/// <summary>
/// In-memory category service for view-model tests: serves a seeded category list and
/// records manual re-categorisation and recategorise-existing calls so tests can assert
/// the view model drives them.
/// </summary>
internal sealed class FakeCategoryService : ICategoryService
{
    private readonly IReadOnlyList<CategoryListItem> _categories;

    public FakeCategoryService(params CategoryListItem[] categories) => _categories = [.. categories];

    public int SetCategoryCalls { get; private set; }

    public Guid LastTransactionId { get; private set; }

    public Guid LastCategoryId { get; private set; }

    /// <summary>Normalised description handed back from <see cref="SetCategoryAsync"/>.</summary>
    public string? NormalizedDescription { get; set; }

    public int RecategorizeCalls { get; private set; }

    public int RecategorizeResult { get; set; }

    public Exception? Throw { get; set; }

    public Task<IReadOnlyList<CategoryListItem>> GetCategoriesAsync(CancellationToken ct) =>
        Task.FromResult(_categories);

    public Task<string?> SetCategoryAsync(Guid transactionId, Guid categoryId, CancellationToken ct)
    {
        SetCategoryCalls++;
        LastTransactionId = transactionId;
        LastCategoryId = categoryId;

        if (Throw is not null)
        {
            throw Throw;
        }

        return Task.FromResult(NormalizedDescription);
    }

    public Task<int> RecategorizeUncategorizedAsync(CancellationToken ct)
    {
        RecategorizeCalls++;

        if (Throw is not null)
        {
            throw Throw;
        }

        return Task.FromResult(RecategorizeResult);
    }
}
