using Coffer.Core.Spending;
using Coffer.Core.Transactions;

namespace Coffer.Application.Tests.Fakes;

/// <summary>
/// In-memory <see cref="ISpendingExplorerQuery"/>: serves seeded per-level results and records the last
/// arguments each level was called with, so the view-model's window/account scoping and drill-down wiring
/// can be asserted without a database.
/// </summary>
internal sealed class FakeSpendingExplorerQuery : ISpendingExplorerQuery
{
    public List<CategorySpend> Categories { get; } = [];

    public List<MerchantSpend> Merchants { get; } = [];

    public List<TransactionListItem> Transactions { get; } = [];

    public SpendingWindow? LastWindow { get; private set; }

    public Guid? LastAccountId { get; private set; }

    public Guid? LastCategoryId { get; private set; }

    public string? LastMerchant { get; private set; }

    public int CategoryCalls { get; private set; }

    public int MerchantCalls { get; private set; }

    public int TransactionCalls { get; private set; }

    public Task<IReadOnlyList<CategorySpend>> GetCategoriesAsync(
        SpendingWindow window, Guid? accountId, CancellationToken ct)
    {
        CategoryCalls++;
        LastWindow = window;
        LastAccountId = accountId;
        return Task.FromResult<IReadOnlyList<CategorySpend>>([.. Categories]);
    }

    public Task<IReadOnlyList<MerchantSpend>> GetMerchantsAsync(
        SpendingWindow window, Guid? categoryId, Guid? accountId, CancellationToken ct)
    {
        MerchantCalls++;
        LastWindow = window;
        LastCategoryId = categoryId;
        LastAccountId = accountId;
        return Task.FromResult<IReadOnlyList<MerchantSpend>>([.. Merchants]);
    }

    public Task<IReadOnlyList<TransactionListItem>> GetTransactionsAsync(
        SpendingWindow window, Guid? categoryId, string? merchant, Guid? accountId, CancellationToken ct)
    {
        TransactionCalls++;
        LastWindow = window;
        LastCategoryId = categoryId;
        LastMerchant = merchant;
        LastAccountId = accountId;
        return Task.FromResult<IReadOnlyList<TransactionListItem>>([.. Transactions]);
    }
}
