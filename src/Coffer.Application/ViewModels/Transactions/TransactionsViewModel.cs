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
    /// <summary>
    /// Sentinel row letting the user clear the account filter (a placeholder only shows
    /// while nothing is selected, so without this there is no way back to "all accounts").
    /// </summary>
    public static readonly AccountListItem AllAccounts = new(Guid.Empty, "Wszystkie konta", "");

    private readonly IGetTransactionsQuery _query;
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

    /// <summary>
    /// Navigation entry point: refresh the account filter (so accounts created inline on the
    /// Import page show up) and then load the rows.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        await LoadAccountsAsync(ct).ConfigureAwait(true);
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
                        Transactions.Add(item);
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

    private TransactionQueryFilter BuildFilter()
    {
        var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var accountId = SelectedAccount is { Id: var id } && id != Guid.Empty ? id : (Guid?)null;

        // A null From would be read as the query's six-month default, so "Cały okres"
        // must pass an explicit floor rather than null.
        DateOnly? from = SelectedRange.Months is { } months
            ? DateOnly.FromDateTime(DateTime.Now).AddMonths(-months)
            : DateOnly.MinValue;

        return new TransactionQueryFilter(From: from, Search: search, AccountId: accountId);
    }

    partial void OnSearchTextChanged(string value) => QueueReload();

    partial void OnSelectedAccountChanged(AccountListItem? value) => QueueReload();

    partial void OnSelectedRangeChanged(DateRangeOption value) => QueueReload();

    private void QueueReload() => ReloadCommand.Execute(null);
}
