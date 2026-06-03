using System.Collections.ObjectModel;
using Coffer.Core.Transactions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Transactions;

/// <summary>
/// View-model behind the Transactions page: a filtered, newest-first list of
/// transactions. Filters (search text, account, date range) re-run the query as
/// they change.
/// </summary>
public sealed partial class TransactionsViewModel : ObservableObject
{
    private readonly IGetTransactionsQuery _query;
    private readonly ILogger<TransactionsViewModel> _logger;

    private bool _accountsLoaded;
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
    private DateRangeOption _selectedRange;

    public TransactionsViewModel(IGetTransactionsQuery query, ILogger<TransactionsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(logger);

        _query = query;
        _logger = logger;

        // Assign the backing field directly so the generated setter's OnSelectedRangeChanged
        // hook does not queue a load before the page is navigated to.
        _selectedRange = DateRangeOption.SixMonths;
    }

    public ObservableCollection<TransactionListItem> Transactions { get; } = [];

    public ObservableCollection<AccountListItem> Accounts { get; } = [];

    public IReadOnlyList<DateRangeOption> DateRanges => DateRangeOption.Options;

    public bool IsEmpty => !IsLoading && Transactions.Count == 0;

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        if (IsLoading)
        {
            // A filter changed mid-load; remember to run once more when this finishes
            // so the latest filter values are not dropped.
            _reloadRequested = true;
            return;
        }

        IsLoading = true;
        OnPropertyChanged(nameof(IsEmpty));
        ErrorMessage = "";
        try
        {
            if (!_accountsLoaded)
            {
                await LoadAccountsAsync(ct).ConfigureAwait(true);
            }

            var items = await _query
                .ExecuteAsync(BuildFilter(), ct)
                .ConfigureAwait(true);

            Transactions.Clear();
            foreach (var item in items)
            {
                Transactions.Add(item);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load transactions");
            ErrorMessage = "Nie udało się wczytać transakcji. Spróbuj ponownie.";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));

            if (_reloadRequested)
            {
                _reloadRequested = false;
                LoadCommand.Execute(null);
            }
        }
    }

    private async Task LoadAccountsAsync(CancellationToken ct)
    {
        var accounts = await _query.GetAccountsAsync(ct).ConfigureAwait(true);

        Accounts.Clear();
        foreach (var account in accounts)
        {
            Accounts.Add(account);
        }

        _accountsLoaded = true;
    }

    private TransactionQueryFilter BuildFilter()
    {
        var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        DateOnly? from = SelectedRange.Months is { } months
            ? DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-months)
            : DateOnly.MinValue;

        return new TransactionQueryFilter(
            From: from,
            Search: search,
            AccountId: SelectedAccount?.Id);
    }

    partial void OnSearchTextChanged(string value) => QueueReload();

    partial void OnSelectedAccountChanged(AccountListItem? value) => QueueReload();

    partial void OnSelectedRangeChanged(DateRangeOption value) => QueueReload();

    private void QueueReload() => LoadCommand.Execute(null);
}
