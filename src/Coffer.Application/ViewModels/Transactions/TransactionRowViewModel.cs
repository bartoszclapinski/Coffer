using Coffer.Core.Categorization;
using Coffer.Core.Transactions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Coffer.Application.ViewModels.Transactions;

/// <summary>
/// One row in the transactions grid. Wraps the read-only <see cref="TransactionListItem"/>
/// and adds an editable category selection: picking a different <see cref="SelectedCategory"/>
/// raises a callback the page view-model turns into a manual re-categorisation (which also
/// teaches the cache). The shared <see cref="Categories"/> list backs the per-row picker.
/// </summary>
public sealed partial class TransactionRowViewModel : ObservableObject
{
    private readonly Func<TransactionRowViewModel, CategoryListItem, Task> _onCategoryChosen;
    private bool _suppressCallback;

    [ObservableProperty]
    private CategoryListItem? _selectedCategory;

    public TransactionRowViewModel(
        TransactionListItem item,
        IReadOnlyList<CategoryListItem> categories,
        Func<TransactionRowViewModel, CategoryListItem, Task> onCategoryChosen)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(onCategoryChosen);

        Id = item.Id;
        Date = item.Date;
        Description = item.Description;
        Merchant = item.Merchant;
        Amount = item.Amount;
        Currency = item.Currency;
        AccountName = item.AccountName;
        Categories = categories;
        _onCategoryChosen = onCategoryChosen;

        _suppressCallback = true;
        _selectedCategory = item.CategoryId is { } id
            ? categories.FirstOrDefault(c => c.Id == id)
            : null;
        _suppressCallback = false;
    }

    public Guid Id { get; }

    public DateOnly Date { get; }

    public string Description { get; }

    public string? Merchant { get; }

    public decimal Amount { get; }

    public string Currency { get; }

    public string AccountName { get; }

    public IReadOnlyList<CategoryListItem> Categories { get; }

    /// <summary>Sets the selection without raising the re-categorisation callback (used to
    /// reflect a server-side change back into the UI).</summary>
    public void SetCategorySilently(CategoryListItem? category)
    {
        _suppressCallback = true;
        SelectedCategory = category;
        _suppressCallback = false;
    }

    partial void OnSelectedCategoryChanged(CategoryListItem? value)
    {
        if (_suppressCallback || value is null)
        {
            return;
        }

        _ = _onCategoryChosen(this, value);
    }
}
