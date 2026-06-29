using System.Collections.ObjectModel;
using System.Globalization;
using Coffer.Application.Localization;
using Coffer.Core.Dashboard;
using Coffer.Core.Transactions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Coffer.Application.ViewModels.Dashboard;

/// <summary>
/// View-model behind the Dashboard landing page. One <see cref="IDashboardQuery"/>
/// call resolves the whole <see cref="DashboardSnapshot"/> (KPIs, trends, top
/// categories, recent transactions) server-side; this VM only shapes those DTOs into
/// LiveCharts series for the view. Series are built here (not in the view) so they stay
/// testable and reusable by the future MAUI dashboard.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private static readonly SKColor _spendStroke = SKColor.Parse("#1D4ED8");
    private static readonly SKColor _spendFill = SKColor.Parse("#1D4ED8").WithAlpha(40);
    private static readonly SKColor _monthlyFill = SKColor.Parse("#1D4ED8");

    private readonly IDashboardQuery _query;
    private readonly ILocalizer _localizer;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private decimal _spend;

    [ObservableProperty]
    private decimal _income;

    [ObservableProperty]
    private decimal _net;

    [ObservableProperty]
    private string _currency = "PLN";

    [ObservableProperty]
    private int _transactionCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleText))]
    private string _currentMonthLabel = "";

    [ObservableProperty]
    private ISeries[] _dailySpendSeries = [];

    [ObservableProperty]
    private ISeries[] _monthlySpendSeries = [];

    [ObservableProperty]
    private ISeries[] _categorySeries = [];

    [ObservableProperty]
    private Axis[] _dailyXAxes = [];

    [ObservableProperty]
    private Axis[] _monthlyXAxes = [];

    public DashboardViewModel(IDashboardQuery query, ILocalizer localizer, ILogger<DashboardViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _query = query;
        _localizer = localizer;
        _logger = logger;
    }

    public ObservableCollection<TransactionListItem> RecentTransactions { get; } = [];

    public ObservableCollection<CategorySlice> TopCategories { get; } = [];

    public bool IsEmpty => !IsLoading && !HasData && string.IsNullOrEmpty(ErrorMessage);

    public string SubtitleText => _localizer.Format("Dashboard.Subtitle", CurrentMonthLabel);

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        OnPropertyChanged(nameof(IsEmpty));
        ErrorMessage = "";
        try
        {
            var snapshot = await _query
                .GetSnapshotAsync(new DashboardFilter(), ct)
                .ConfigureAwait(true);

            Apply(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard");
            ErrorMessage = _localizer["Dashboard.Error"];
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private void Apply(DashboardSnapshot snapshot)
    {
        HasData = snapshot.HasData;

        Spend = snapshot.CurrentMonth.Spend;
        Income = snapshot.CurrentMonth.Income;
        Net = snapshot.CurrentMonth.Net;
        Currency = snapshot.CurrentMonth.Currency;
        TransactionCount = snapshot.CurrentMonth.TransactionCount;
        CurrentMonthLabel = snapshot.CurrentMonth.Month.ToString("MMMM yyyy", new CultureInfo("pl-PL"));

        DailySpendSeries =
        [
            new LineSeries<decimal>
            {
                Name = _localizer["Dashboard.Spend"],
                Values = snapshot.DailySpend.Select(p => p.Total).ToArray(),
                Stroke = new SolidColorPaint(_spendStroke, 2),
                Fill = new SolidColorPaint(_spendFill),
                GeometryStroke = null,
                GeometryFill = null,
                GeometrySize = 0,
                LineSmoothness = 0.5,
            },
        ];
        DailyXAxes =
        [
            new Axis
            {
                Labels = snapshot.DailySpend.Select(p => p.Date.ToString("dd.MM", CultureInfo.InvariantCulture)).ToArray(),
                LabelsRotation = 0,
                MinStep = 1,
            },
        ];

        MonthlySpendSeries =
        [
            new ColumnSeries<decimal>
            {
                Name = _localizer["Dashboard.Spend"],
                Values = snapshot.MonthlySpend.Select(p => p.Total).ToArray(),
                Fill = new SolidColorPaint(_monthlyFill),
            },
        ];
        MonthlyXAxes =
        [
            new Axis
            {
                Labels = snapshot.MonthlySpend
                    .Select(p => p.Date.ToString("MMM yy", new CultureInfo("pl-PL")))
                    .ToArray(),
            },
        ];

        CategorySeries = snapshot.TopCategories
            .Select(slice => (ISeries)new PieSeries<decimal>
            {
                Name = slice.Name,
                Values = [slice.Total],
                Fill = new SolidColorPaint(SKColor.Parse(slice.Color)),
                InnerRadius = 60,
            })
            .ToArray();

        TopCategories.Clear();
        foreach (var slice in snapshot.TopCategories)
        {
            TopCategories.Add(slice);
        }

        RecentTransactions.Clear();
        foreach (var item in snapshot.RecentTransactions)
        {
            RecentTransactions.Add(item);
        }
    }
}
