using System.Collections.ObjectModel;
using Coffer.Application.Localization;
using Coffer.Application.ViewModels.Planning;
using Coffer.Core.Budgeting;
using Coffer.Core.Forecasting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Forecast;

/// <summary>
/// View-model behind the "Prognoza / Forecast" page. For the next calendar month it shows each category's
/// predicted spend — a fixed part (recurring outflows landing next month) plus a variable part (recent
/// discretionary history) — and a suggested monthly budget limit shown against the category's current
/// limit, which the owner can accept with one click (upserting via <see cref="ICategoryBudgetRepository"/>).
/// Every figure comes from the deterministic <see cref="ExpenseForecastEngine"/> via
/// <see cref="IExpenseForecastQuery"/>; this VM only assembles and formats. No AI.
/// </summary>
public sealed partial class ForecastViewModel : ObservableObject
{
    private const string Currency = "PLN";

    private readonly IExpenseForecastQuery _query;
    private readonly ICategoryBudgetRepository _budgets;
    private readonly ILocalizer _localizer;
    private readonly ILogger<ForecastViewModel> _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _monthText = "";

    [ObservableProperty]
    private string _totalText = "";

    [ObservableProperty]
    private bool _hasForecast;

    [ObservableProperty]
    private bool _isEmpty;

    public ForecastViewModel(
        IExpenseForecastQuery query,
        ICategoryBudgetRepository budgets,
        ILocalizer localizer,
        ILogger<ForecastViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(budgets);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _query = query;
        _budgets = budgets;
        _localizer = localizer;
        _logger = logger;
    }

    public ObservableCollection<ForecastRow> Forecasts { get; } = [];

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            await RefreshAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the forecast");
            ErrorMessage = _localizer["Forecast.Error.Load"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AcceptSuggestionAsync(ForecastRow? row, CancellationToken ct)
    {
        if (row is null || row.CategoryId is not { } categoryId)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = "";
        try
        {
            await _budgets.SetBudgetAsync(categoryId, row.SuggestedLimit, Currency, ct).ConfigureAwait(true);
            await RefreshAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set a budget from a forecast suggestion");
            ErrorMessage = _localizer["Forecast.Error.Save"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var forecast = await _query.GetForecastAsync(null, ct).ConfigureAwait(true);

        MonthText = CashFlowDisplay.AccrualPeriod(forecast.Month);
        TotalText = CashFlowDisplay.Money(forecast.Total);

        Forecasts.Clear();
        foreach (var line in forecast.Categories)
        {
            var isCategory = line.CategoryId is not null;
            Forecasts.Add(new ForecastRow(
                line.CategoryId,
                line.CategoryName ?? _localizer["Forecast.Uncategorized"],
                line.CategoryColor,
                CashFlowDisplay.Money(line.Fixed),
                CashFlowDisplay.Money(line.Variable),
                CashFlowDisplay.Money(line.Total),
                CashFlowDisplay.Money(line.SuggestedLimit),
                line.CurrentLimit is { } limit ? CashFlowDisplay.Money(limit) : _localizer["Forecast.NoLimit"],
                line.SuggestedLimit,
                isCategory));
        }

        HasForecast = Forecasts.Count > 0;
        IsEmpty = !HasForecast;
    }
}

/// <summary>
/// A category's next-month forecast row: formatted fixed/variable/total, the suggested limit and its raw
/// value (for the one-click set-as-budget action), the current limit caption, and whether the row can be
/// turned into a budget (false for the uncategorised bucket, which has no category to budget).
/// </summary>
public sealed record ForecastRow(
    Guid? CategoryId,
    string CategoryName,
    string? Color,
    string FixedText,
    string VariableText,
    string TotalText,
    string SuggestedText,
    string CurrentLimitText,
    decimal SuggestedLimit,
    bool CanAccept);
