using System.Collections.ObjectModel;
using Coffer.Core.Categorization;
using Coffer.Core.Transactions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Transactions;

/// <summary>
/// View-model behind the Transactions page: a filtered, newest-first list of
/// transactions. Filters (search text, account, category, date range) re-run the query
/// as they change. Each row carries an inline category picker for manual
/// re-categorisation, and a command recategorises already-imported uncategorised rows.
/// </summary>
public sealed partial class TransactionsViewModel : ObservableObject
{
    /// <summary>
    /// Sentinel row letting the user clear the account filter (a placeholder only shows
    /// while nothing is selected, so without this there is no way back to "all accounts").
    /// </summary>
    public static readonly AccountListItem AllAccounts = new(Guid.Empty, "Wszystkie konta", "");

    /// <summary>Sentinel letting the user clear the category filter (see <see cref="AllAccounts"/>).</summary>
    public static readonly CategoryListItem AllCategories = new(Guid.Empty, "Wszystkie kategorie", "");

    private readonly IGetTransactionsQuery _query;
    private readonly ICategoryService _categoryService;
    private readonly ILogger<TransactionsViewModel> _logger;

    private bool _reloadRequested;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private AccountListItem? _selectedAccount;

    [ObservableProperty]
    private CategoryListItem? _selectedCategoryFilter;

    [ObservableProperty]
    private DateRangeOption _selectedRange;

    public TransactionsViewModel(
        IGetTransactionsQuery query,
        ICategoryService categoryService,
        ILogger<TransactionsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(categoryService);
        ArgumentNullException.ThrowIfNull(logger);

        _query = query;
        _categoryService = categoryService;
        _logger = logger;

        // Assign the backing field directly so the generated setter's OnSelectedRangeChanged
        // hook does not queue a load before the page is navigated to.
        _selectedRange = DateRangeOption.SixMonths;
    }

    public ObservableCollection<TransactionRowViewModel> Transactions { get; } = [];

    public ObservableCollection<AccountListItem> Accounts { get; } = [];

    /// <summary>Categories for the per-row picker (no sentinel).</summary>
    public ObservableCollection<CategoryListItem> Categories { get; } = [];

    /// <summary>Categories for the filter dropdown, led by the "all categories" sentinel.</summary>
    public ObservableCollection<CategoryListItem> CategoryFilters { get; } = [];

    public IReadOnlyList<DateRangeOption> DateRanges => DateRangeOption.Options;

    public bool IsEmpty => !IsLoading && Transactions.Count == 0;

    /// <summary>
    /// Navigation entry point: refresh the account/category filters (so inline-created
    /// accounts and freshly seeded categories show up) and then load the rows.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        await LoadAccountsAsync(ct).ConfigureAwait(true);
        await LoadCategoriesAsync(ct).ConfigureAwait(true);
        await ReloadAsync(ct).ConfigureAwait(true);
    }

    /// <summary>
    /// Re-runs the transactions query for the current filters. Concurrent execution is
    /// allowed so a filter changed mid-load re-enters here, trips the <see cref="IsLoading"/>
    /// guard, and is coalesced into one trailing reload that reads the latest filter values.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ReloadAsync(CancellationToken ct)
    {
        if (IsLoading)
        {
            _reloadRequested = true;
            return;
        }

        IsLoading = true;
        OnPropertyChanged(nameof(IsEmpty));
        try
        {
            do
            {
                _reloadRequested = false;
                ErrorMessage = "";
                try
                {
                    var items = await _query
                        .ExecuteAsync(BuildFilter(), ct)
                        .ConfigureAwait(true);

                    Transactions.Clear();
                    foreach (var item in items)
                    {
                        Transactions.Add(new TransactionRowViewModel(item, Categories, OnRowCategoryChosenAsync));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load transactions");
                    ErrorMessage = "Nie udało się wczytać transakcji. Spróbuj ponownie.";
                }
            }
            while (_reloadRequested);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>
    /// Re-runs the deterministic categoriser over already-imported uncategorised rows, then
    /// reloads so the new categories show. Lets the owner categorise the backlog without a re-import.
    /// </summary>
    [RelayCommand]
    private async Task RecategorizeExistingAsync(CancellationToken ct)
    {
        try
        {
            var count = await _categoryService
                .RecategorizeUncategorizedAsync(ct)
                .ConfigureAwait(true);
            _logger.LogInformation("Recategorised {Count} existing transaction(s)", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recategorise existing transactions");
            ErrorMessage = "Nie udało się skategoryzować istniejących transakcji.";
            return;
        }

        await ReloadAsync(ct).ConfigureAwait(true);
    }

    private async Task OnRowCategoryChosenAsync(TransactionRowViewModel row, CategoryListItem category)
    {
        try
        {
            await _categoryService
                .SetCategoryAsync(row.Id, category.Id, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set category for transaction {TransactionId}", row.Id);
            ErrorMessage = "Nie udało się zmienić kategorii. Spróbuj ponownie.";
        }
    }

    private async Task LoadAccountsAsync(CancellationToken ct)
    {
        var accounts = await _query.GetAccountsAsync(ct).ConfigureAwait(true);

        Accounts.Clear();
        Accounts.Add(AllAccounts);
        foreach (var account in accounts)
        {
            Accounts.Add(account);
        }
    }

    private async Task LoadCategoriesAsync(CancellationToken ct)
    {
        var categories = await _categoryService.GetCategoriesAsync(ct).ConfigureAwait(true);

        Categories.Clear();
        CategoryFilters.Clear();
        CategoryFilters.Add(AllCategories);
        foreach (var category in categories)
        {
            Categories.Add(category);
            CategoryFilters.Add(category);
        }
    }

    private TransactionQueryFilter BuildFilter()
    {
        var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var accountId = SelectedAccount is { Id: var id } && id != Guid.Empty ? id : (Guid?)null;
        var categoryId = SelectedCategoryFilter is { Id: var cid } && cid != Guid.Empty ? cid : (Guid?)null;

        // A null From would be read as the query's six-month default, so "Cały okres"
        // must pass an explicit floor rather than null.
        DateOnly? from = SelectedRange.Months is { } months
            ? DateOnly.FromDateTime(DateTime.Now).AddMonths(-months)
            : DateOnly.MinValue;

        return new TransactionQueryFilter(From: from, Search: search, AccountId: accountId, CategoryId: categoryId);
    }

    partial void OnSearchTextChanged(string value) => QueueReload();

    partial void OnSelectedAccountChanged(AccountListItem? value) => QueueReload();

    partial void OnSelectedCategoryFilterChanged(CategoryListItem? value) => QueueReload();

    partial void OnSelectedRangeChanged(DateRangeOption value) => QueueReload();

    private void QueueReload() => ReloadCommand.Execute(null);
}
