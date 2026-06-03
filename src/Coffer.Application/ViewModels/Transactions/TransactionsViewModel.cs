using System.Collections.ObjectModel;
using Coffer.Core.Transactions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Transactions;

/// <summary>
/// View-model behind the Transactions page. Sprint 9-C ships the load path and an
/// empty-state surface so the shell can navigate to it; Sprint 9-D adds the filter
/// bar and the DataGrid columns.
/// </summary>
public sealed partial class TransactionsViewModel : ObservableObject
{
    private readonly IGetTransactionsQuery _query;
    private readonly ILogger<TransactionsViewModel> _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    public TransactionsViewModel(IGetTransactionsQuery query, ILogger<TransactionsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(logger);

        _query = query;
        _logger = logger;
    }

    public ObservableCollection<TransactionListItem> Transactions { get; } = [];

    public bool IsEmpty => !IsLoading && Transactions.Count == 0;

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var items = await _query
                .ExecuteAsync(new TransactionQueryFilter(), ct)
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
        }
    }
}
