using System.Collections.ObjectModel;
using System.Globalization;
using Coffer.Application.Localization;
using Coffer.Application.ViewModels.Planning;
using Coffer.Core.Accounts;
using Coffer.Core.Spending;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Spending;

/// <summary>
/// View-model behind the interactive "Wydatki / Spending" page. The owner picks a time window (a preset
/// or a custom range) and an optional account, then explores spend by drilling down:
/// category → merchant → the underlying transactions, with a breadcrumb to step back. Every figure comes
/// from the server-side <see cref="ISpendingExplorerQuery"/> (debits as positive PLN magnitudes); this VM
/// only assembles the window, formats money via <see cref="CashFlowDisplay"/>, and localises the
/// uncategorised / unknown-merchant fallback labels at the boundary — keeping <c>Coffer.Core</c>
/// presentation-free. No AI, no migration: pure read-side.
/// </summary>
public sealed partial class SpendingExplorerViewModel : ObservableObject
{
    private readonly ISpendingExplorerQuery _query;
    private readonly IAccountService _accountService;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SpendingExplorerViewModel> _logger;

    private SpendingWindow _window = new(DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today));

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private SpendingAccountOption? _selectedAccount;

    [ObservableProperty]
    private SpendingPresetOption? _selectedPreset;

    [ObservableProperty]
    private DateTimeOffset? _customFrom;

    [ObservableProperty]
    private DateTimeOffset? _customTo;

    [ObservableProperty]
    private string _windowText = "";

    [ObservableProperty]
    private string _totalText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCategoriesLevel))]
    [NotifyPropertyChangedFor(nameof(IsMerchantsLevel))]
    [NotifyPropertyChangedFor(nameof(IsTransactionsLevel))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    private SpendingDrillLevel _level = SpendingDrillLevel.Categories;

    [ObservableProperty]
    private bool _isCustomWindow;

    [ObservableProperty]
    private SpendingCategoryRow? _selectedCategory;

    [ObservableProperty]
    private SpendingMerchantRow? _selectedMerchant;

    [ObservableProperty]
    private bool _isEmpty;

    public SpendingExplorerViewModel(
        ISpendingExplorerQuery query,
        IAccountService accountService,
        ILocalizer localizer,
        ILogger<SpendingExplorerViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(accountService);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _query = query;
        _accountService = accountService;
        _localizer = localizer;
        _logger = logger;
    }

    public ObservableCollection<SpendingAccountOption> Accounts { get; } = [];

    public ObservableCollection<SpendingPresetOption> Presets { get; } = [];

    public ObservableCollection<SpendingCategoryRow> Categories { get; } = [];

    public ObservableCollection<SpendingMerchantRow> Merchants { get; } = [];

    public ObservableCollection<SpendingTransactionRow> Transactions { get; } = [];

    public bool IsCategoriesLevel => Level == SpendingDrillLevel.Categories;

    public bool IsMerchantsLevel => Level == SpendingDrillLevel.Merchants;

    public bool IsTransactionsLevel => Level == SpendingDrillLevel.Transactions;

    public bool CanGoBack => Level != SpendingDrillLevel.Categories;

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var accounts = await _accountService.GetAllWithAnchorsAsync(ct).ConfigureAwait(true);

            var previousAccount = SelectedAccount?.Id;
            Accounts.Clear();
            Accounts.Add(new SpendingAccountOption(null, _localizer["Spending.AllAccounts"]));
            foreach (var a in accounts)
            {
                Accounts.Add(new SpendingAccountOption(a.Id, a.Name));
            }

            SelectedAccount = Accounts.FirstOrDefault(o => o.Id == previousAccount) ?? Accounts[0];

            if (Presets.Count == 0)
            {
                foreach (var preset in Enum.GetValues<SpendingWindowPreset>())
                {
                    Presets.Add(new SpendingPresetOption(preset, _localizer[PresetKey(preset)]));
                }

                SelectedPreset = Presets.First(p => p.Preset == SpendingWindowPreset.LastMonth);
            }

            await ApplyWindowAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the spending explorer");
            ErrorMessage = _localizer["Spending.Error.Load"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ApplyWindowAsync(CancellationToken ct)
    {
        var preset = SelectedPreset?.Preset ?? SpendingWindowPreset.LastMonth;
        IsCustomWindow = preset == SpendingWindowPreset.Custom;

        _window = SpendingWindowResolver.Resolve(
            preset,
            DateOnly.FromDateTime(DateTime.Today),
            CustomFrom is { } from ? DateOnly.FromDateTime(from.Date) : null,
            CustomTo is { } to ? DateOnly.FromDateTime(to.Date) : null);
        WindowText = $"{CashFlowDisplay.Date(_window.From)} – {CashFlowDisplay.Date(_window.To)}";

        SelectedCategory = null;
        SelectedMerchant = null;
        Merchants.Clear();
        Transactions.Clear();
        Level = SpendingDrillLevel.Categories;

        await LoadCategoriesAsync(ct).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SelectCategoryAsync(SpendingCategoryRow? row, CancellationToken ct)
    {
        if (row is null)
        {
            return;
        }

        SelectedCategory = row;
        SelectedMerchant = null;
        Transactions.Clear();
        Level = SpendingDrillLevel.Merchants;
        TotalText = row.TotalText;

        await LoadMerchantsAsync(row, ct).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SelectMerchantAsync(SpendingMerchantRow? row, CancellationToken ct)
    {
        if (row is null || SelectedCategory is null)
        {
            return;
        }

        SelectedMerchant = row;
        Level = SpendingDrillLevel.Transactions;
        TotalText = row.TotalText;

        await LoadTransactionsAsync(SelectedCategory, row, ct).ConfigureAwait(true);
    }

    [RelayCommand]
    private void Back()
    {
        switch (Level)
        {
            case SpendingDrillLevel.Transactions:
                SelectedMerchant = null;
                Transactions.Clear();
                Level = SpendingDrillLevel.Merchants;
                TotalText = SelectedCategory?.TotalText ?? "";
                IsEmpty = Merchants.Count == 0;
                break;
            case SpendingDrillLevel.Merchants:
                SelectedCategory = null;
                Merchants.Clear();
                Level = SpendingDrillLevel.Categories;
                TotalText = _categoriesTotalText;
                IsEmpty = Categories.Count == 0;
                break;
        }
    }

    private string _categoriesTotalText = "";

    private async Task LoadCategoriesAsync(CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var categories = await _query.GetCategoriesAsync(_window, SelectedAccount?.Id, ct).ConfigureAwait(true);

            Categories.Clear();
            var total = 0m;
            foreach (var c in categories)
            {
                total += c.Total;
                Categories.Add(new SpendingCategoryRow(
                    c.CategoryId,
                    c.CategoryName ?? _localizer["Spending.Uncategorized"],
                    c.CategoryColor,
                    CashFlowDisplay.Money(c.Total),
                    (double)(c.Share * 100m),
                    Percent(c.Share),
                    c.Count));
            }

            _categoriesTotalText = CashFlowDisplay.Money(total);
            TotalText = _categoriesTotalText;
            IsEmpty = Categories.Count == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load spending categories");
            ErrorMessage = _localizer["Spending.Error.Load"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadMerchantsAsync(SpendingCategoryRow category, CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var merchants = await _query.GetMerchantsAsync(_window, category.CategoryId, SelectedAccount?.Id, ct)
                .ConfigureAwait(true);

            Merchants.Clear();
            foreach (var m in merchants)
            {
                Merchants.Add(new SpendingMerchantRow(
                    m.Merchant,
                    m.Merchant ?? _localizer["Spending.UnknownMerchant"],
                    CashFlowDisplay.Money(m.Total),
                    (double)(m.Share * 100m),
                    Percent(m.Share),
                    m.Count));
            }

            IsEmpty = Merchants.Count == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load spending merchants");
            ErrorMessage = _localizer["Spending.Error.Load"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadTransactionsAsync(
        SpendingCategoryRow category, SpendingMerchantRow merchant, CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var transactions = await _query
                .GetTransactionsAsync(_window, category.CategoryId, merchant.Merchant, SelectedAccount?.Id, ct)
                .ConfigureAwait(true);

            Transactions.Clear();
            foreach (var t in transactions)
            {
                Transactions.Add(new SpendingTransactionRow(
                    t.Id,
                    CashFlowDisplay.Date(t.Date),
                    t.Description,
                    CashFlowDisplay.Money(Math.Abs(t.Amount))));
            }

            IsEmpty = Transactions.Count == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load spending transactions");
            ErrorMessage = _localizer["Spending.Error.Load"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string Percent(decimal share) =>
        Math.Round((double)(share * 100m)).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string PresetKey(SpendingWindowPreset preset) => preset switch
    {
        SpendingWindowPreset.ThisMonth => "Spending.Window.ThisMonth",
        SpendingWindowPreset.LastMonth => "Spending.Window.LastMonth",
        SpendingWindowPreset.Last3Months => "Spending.Window.Last3Months",
        SpendingWindowPreset.Last12Months => "Spending.Window.Last12Months",
        SpendingWindowPreset.ThisYear => "Spending.Window.ThisYear",
        SpendingWindowPreset.Custom => "Spending.Window.Custom",
        _ => preset.ToString(),
    };
}

/// <summary>An account choice for the spending explorer (null id = all accounts).</summary>
public sealed record SpendingAccountOption(Guid? Id, string Label);

/// <summary>A selectable window preset with its localized caption.</summary>
public sealed record SpendingPresetOption(SpendingWindowPreset Preset, string Label);

/// <summary>A category row for the top level: its resolved label/colour, formatted spend, and share.</summary>
public sealed record SpendingCategoryRow(
    Guid? CategoryId, string Label, string? Color, string TotalText, double SharePercent, string ShareText, int Count);

/// <summary>A merchant row within a drilled-into category.</summary>
public sealed record SpendingMerchantRow(
    string? Merchant, string Label, string TotalText, double SharePercent, string ShareText, int Count);

/// <summary>A transaction row at the leaf level (spend shown as a positive magnitude).</summary>
public sealed record SpendingTransactionRow(Guid Id, string DateText, string Title, string AmountText);
