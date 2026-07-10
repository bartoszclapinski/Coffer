using Coffer.Core.Categorization;
using Coffer.Core.Transactions;

namespace Coffer.Desktop.Preview;

/// <summary>Dev-only canned <see cref="IGetTransactionsQuery"/> for the Transactions preview.</summary>
internal sealed class PreviewTransactionsQuery : IGetTransactionsQuery
{
    public Task<IReadOnlyList<TransactionListItem>> ExecuteAsync(TransactionQueryFilter filter, CancellationToken ct)
    {
        var acc = Guid.NewGuid();
        IReadOnlyList<TransactionListItem> rows =
        [
            Row(acc, "Biedronka", "Biedronka", -86.40m, "Groceries", "#1C6E6A", 8),
            Row(acc, "Uber", "Uber", -18.30m, "Transport", "#3D5AA6", 8),
            Row(acc, "Sweetgreen", "Sweetgreen", -16.80m, "Dining", "#A8552F", 5),
            Row(acc, "Spotify", "Spotify", -23.99m, "Subscriptions", "#7A4A7E", 3),
            Row(acc, "Netflix", "Netflix", -43.00m, "Subscriptions", "#7A4A7E", 2),
            Row(acc, "Orlen", "Orlen", -212.50m, "Transport", "#3D5AA6", 2),
            Row(acc, "Rossmann", "Rossmann", -64.20m, "Shopping", "#8A6D3B", 1),
            Row(acc, "Wynagrodzenie", "Northwind", 8900.00m, "Income", "#2F6B4F", 1),
        ];
        return Task.FromResult(rows);
    }

    public Task<IReadOnlyList<AccountListItem>> GetAccountsAsync(CancellationToken ct)
    {
        IReadOnlyList<AccountListItem> accounts = [new(Guid.NewGuid(), "Konto główne", "PKO")];
        return Task.FromResult(accounts);
    }

    private static TransactionListItem Row(Guid acc, string desc, string merchant, decimal amount, string cat, string color, int day) =>
        new(Guid.NewGuid(), new DateOnly(2026, 7, day), desc, merchant, amount, "PLN", acc, "Konto główne",
            Guid.NewGuid(), cat, color);
}

/// <summary>Dev-only canned <see cref="ICategoryService"/> for the Transactions preview.</summary>
internal sealed class PreviewCategoryService : ICategoryService
{
    public Task<IReadOnlyList<CategoryListItem>> GetCategoriesAsync(CancellationToken ct)
    {
        IReadOnlyList<CategoryListItem> cats =
        [
            new(Guid.NewGuid(), "Groceries", "#1C6E6A"),
            new(Guid.NewGuid(), "Dining", "#A8552F"),
            new(Guid.NewGuid(), "Transport", "#3D5AA6"),
            new(Guid.NewGuid(), "Subscriptions", "#7A4A7E"),
            new(Guid.NewGuid(), "Income", "#2F6B4F"),
        ];
        return Task.FromResult(cats);
    }

    public Task<string?> SetCategoryAsync(Guid transactionId, Guid categoryId, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    public Task<int> RecategorizeUncategorizedAsync(CancellationToken ct) => Task.FromResult(0);
}
