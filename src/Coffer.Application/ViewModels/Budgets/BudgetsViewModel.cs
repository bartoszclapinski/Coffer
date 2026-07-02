using System.Collections.ObjectModel;
using System.Globalization;
using Coffer.Application.Localization;
using Coffer.Application.ViewModels.Planning;
using Coffer.Core.Budgeting;
using Coffer.Core.Categorization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Budgets;

/// <summary>
/// View-model behind the "Budżety / Budgets" page. The owner sets a monthly limit per category and sees,
/// for the current (dashboard-anchored) month, how each budget is tracking — spent vs limit, remaining, a
/// linear end-of-month projection, and an ok/approaching/over zone — plus the categories with spend but no
/// budget (including the uncategorised bucket) so overspending can't hide. Every figure comes from the
/// deterministic <see cref="BudgetTrackingEngine"/> via <see cref="IBudgetTrackingQuery"/>; this VM only
/// assembles inputs and formats output. No AI.
/// </summary>
public sealed partial class BudgetsViewModel : ObservableObject
{
    private const string Currency = "PLN";

    private readonly ICategoryBudgetRepository _repository;
    private readonly IBudgetTrackingQuery _query;
    private readonly ICategoryService _categoryService;
    private readonly ILocalizer _localizer;
    private readonly ILogger<BudgetsViewModel> _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _monthText = "";

    [ObservableProperty]
    private CategoryListItem? _selectedCategory;

    [ObservableProperty]
    private decimal _newLimit;

    [ObservableProperty]
    private bool _hasBudgets;

    [ObservableProperty]
    private bool _hasUnbudgeted;

    [ObservableProperty]
    private bool _isEmpty;

    public BudgetsViewModel(
        ICategoryBudgetRepository repository,
        IBudgetTrackingQuery query,
        ICategoryService categoryService,
        ILocalizer localizer,
        ILogger<BudgetsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(categoryService);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _query = query;
        _categoryService = categoryService;
        _localizer = localizer;
        _logger = logger;
    }

    public ObservableCollection<CategoryListItem> Categories { get; } = [];

    public ObservableCollection<BudgetRow> Budgets { get; } = [];

    public ObservableCollection<UnbudgetedRow> Unbudgeted { get; } = [];

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var categories = await _categoryService.GetCategoriesAsync(ct).ConfigureAwait(true);
            var previous = SelectedCategory?.Id;
            Categories.Clear();
            foreach (var c in categories)
            {
                Categories.Add(c);
            }

            SelectedCategory = Categories.FirstOrDefault(c => c.Id == previous);

            await RefreshOverviewAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load budgets");
            ErrorMessage = _localizer["Budgets.Error.Load"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AddBudgetAsync(CancellationToken ct)
    {
        if (SelectedCategory is null)
        {
            ErrorMessage = _localizer["Budgets.Error.Category"];
            return;
        }

        if (NewLimit <= 0m)
        {
            ErrorMessage = _localizer["Budgets.Error.Limit"];
            return;
        }

        IsLoading = true;
        ErrorMessage = "";
        try
        {
            await _repository.SetBudgetAsync(SelectedCategory.Id, NewLimit, Currency, ct).ConfigureAwait(true);
            NewLimit = 0m;
            await RefreshOverviewAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save a budget");
            ErrorMessage = _localizer["Budgets.Error.Save"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RemoveBudgetAsync(BudgetRow? row, CancellationToken ct)
    {
        if (row is null)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = "";
        try
        {
            await _repository.RemoveAsync(row.CategoryId, ct).ConfigureAwait(true);
            await RefreshOverviewAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove a budget");
            ErrorMessage = _localizer["Budgets.Error.Save"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshOverviewAsync(CancellationToken ct)
    {
        var overview = await _query.GetOverviewAsync(null, ct).ConfigureAwait(true);

        MonthText = CashFlowDisplay.AccrualPeriod(overview.Month);

        Budgets.Clear();
        foreach (var line in overview.Budgets)
        {
            var s = line.Status;
            Budgets.Add(new BudgetRow(
                line.CategoryId,
                line.CategoryName,
                line.CategoryColor,
                CashFlowDisplay.Money(s.Spent),
                CashFlowDisplay.Money(s.Limit),
                CashFlowDisplay.Money(s.Remaining),
                CashFlowDisplay.Money(s.Projected),
                Math.Min(100d, (double)(s.Fraction * 100m)),
                Percent(s.Fraction),
                ZoneLabel(s.Zone),
                ZoneColor(s.Zone)));
        }

        Unbudgeted.Clear();
        foreach (var line in overview.Unbudgeted)
        {
            Unbudgeted.Add(new UnbudgetedRow(
                line.CategoryName ?? _localizer["Budgets.Uncategorized"],
                CashFlowDisplay.Money(line.Spent)));
        }

        HasBudgets = Budgets.Count > 0;
        HasUnbudgeted = Unbudgeted.Count > 0;
        IsEmpty = !HasBudgets && !HasUnbudgeted;
    }

    private string ZoneLabel(BudgetZone zone) => zone switch
    {
        BudgetZone.Ok => _localizer["Budgets.Zone.Ok"],
        BudgetZone.Warning => _localizer["Budgets.Zone.Warning"],
        BudgetZone.Over => _localizer["Budgets.Zone.Over"],
        _ => "",
    };

    private static string ZoneColor(BudgetZone zone) => zone switch
    {
        BudgetZone.Ok => "#34C759",
        BudgetZone.Warning => "#FF9500",
        BudgetZone.Over => "#FF3B30",
        _ => "#8E8E93",
    };

    private static string Percent(decimal fraction) =>
        Math.Round((double)(fraction * 100m)).ToString("0", CultureInfo.InvariantCulture) + "%";
}

/// <summary>A budgeted category's row: formatted figures, a clamped bar value, and its zone caption/colour.</summary>
public sealed record BudgetRow(
    Guid CategoryId,
    string CategoryName,
    string? Color,
    string SpentText,
    string LimitText,
    string RemainingText,
    string ProjectedText,
    double BarValue,
    string PercentText,
    string ZoneLabel,
    string ZoneColor);

/// <summary>A category with month spend but no budget (label already localized for the uncategorised bucket).</summary>
public sealed record UnbudgetedRow(string Label, string SpentText);
