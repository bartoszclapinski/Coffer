using System.Collections.ObjectModel;
using Coffer.Application.Localization;
using Coffer.Core.Domain;
using Coffer.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Coffer.Application.ViewModels.Planning;

/// <summary>
/// View-model behind the cash-flow planning page. Opening it loads the owner's recurring flows, seeds
/// the opening balance from the running sum of imported transactions, runs the deterministic
/// <see cref="CashFlowProjectionEngine"/> forward over the chosen horizon, and surfaces the dated
/// timeline plus the balance curve. Detection only proposes candidates the owner confirms; editing,
/// adding, deleting and confirming all mutate through <see cref="IRecurringFlowRepository"/> and
/// re-project. Changing the horizon re-projects from cached state without re-querying. The engine
/// calculates the timeline; <see cref="ICashFlowExplainer"/> narrates it on demand, falling back to a
/// deterministic summary whenever the AI is unavailable. Re-projecting clears any stale narration.
/// </summary>
public sealed partial class CashFlowPlanningViewModel : ObservableObject
{
    private static readonly SKColor _balanceStroke = SKColor.Parse("#1D4ED8");
    private static readonly SKColor _balanceFill = SKColor.Parse("#1D4ED8").WithAlpha(40);

    private readonly IRecurringFlowRepository _repository;
    private readonly IRecurringFlowDetector _detector;
    private readonly IRunningBalanceQuery _balanceQuery;
    private readonly IStatementContinuityChecker _continuityChecker;
    private readonly CashFlowProjectionEngine _engine;
    private readonly ICashFlowExplainer _explainer;
    private readonly ILocalizer _localizer;
    private readonly ILogger<CashFlowPlanningViewModel> _logger;

    private IReadOnlyList<RecurringFlow> _flows = [];
    private decimal _openingBalance;
    private DateOnly _today;
    private CashFlowProjection? _projection;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private bool _hasSuggestions;

    [ObservableProperty]
    private bool _hasGaps;

    [ObservableProperty]
    private bool _hasTightWindow;

    [ObservableProperty]
    private string _openingBalanceText = "";

    [ObservableProperty]
    private string _closingBalanceText = "";

    [ObservableProperty]
    private string _lowestBalanceText = "";

    [ObservableProperty]
    private string _lowestBalanceDateText = "";

    [ObservableProperty]
    private HorizonOption? _selectedHorizon;

    [ObservableProperty]
    private ISeries[] _balanceSeries = [];

    [ObservableProperty]
    private Axis[] _balanceXAxes = [];

    [ObservableProperty]
    private string _newFlowName = "";

    [ObservableProperty]
    private decimal _newFlowAmount;

    [ObservableProperty]
    private int _newFlowAnchorDay = 1;

    [ObservableProperty]
    private int _newFlowAccrualOffsetMonths;

    [ObservableProperty]
    private CashFlowDirectionOption? _newFlowDirection;

    [ObservableProperty]
    private CashFlowIntervalOption? _newFlowInterval;

    [ObservableProperty]
    private bool _isExplaining;

    [ObservableProperty]
    private bool _hasNarrative;

    [ObservableProperty]
    private bool _narrativeIsAi;

    [ObservableProperty]
    private string _narrative = "";

    public CashFlowPlanningViewModel(
        IRecurringFlowRepository repository,
        IRecurringFlowDetector detector,
        IRunningBalanceQuery balanceQuery,
        IStatementContinuityChecker continuityChecker,
        CashFlowProjectionEngine engine,
        ICashFlowExplainer explainer,
        ILocalizer localizer,
        ILogger<CashFlowPlanningViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(balanceQuery);
        ArgumentNullException.ThrowIfNull(continuityChecker);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(explainer);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _detector = detector;
        _balanceQuery = balanceQuery;
        _continuityChecker = continuityChecker;
        _engine = engine;
        _explainer = explainer;
        _localizer = localizer;
        _logger = logger;

        IntervalOptions = new[] { 1, 3, 12 }
            .Select(m => new CashFlowIntervalOption(m, _localizer[CashFlowDisplay.IntervalKey(m)]))
            .ToArray();
        DirectionOptions = Enum.GetValues<FlowDirection>()
            .Select(d => new CashFlowDirectionOption(d, _localizer[CashFlowDisplay.DirectionKey(d)]))
            .ToArray();
        HorizonOptions = new[] { 30, 90, 180, 365 }
            .Select(days => new HorizonOption(days, _localizer.Format("CashFlow.Horizon.Days", days)))
            .ToArray();

        _selectedHorizon = HorizonOptions[1];
        _newFlowDirection = DirectionOptions[0];
        _newFlowInterval = IntervalOptions[0];
    }

    public ObservableCollection<RecurringFlowViewModel> Flows { get; } = [];

    public ObservableCollection<FlowCandidateViewModel> Suggestions { get; } = [];

    public ObservableCollection<CashFlowEventViewModel> Timeline { get; } = [];

    public ObservableCollection<string> StatementGaps { get; } = [];

    public IReadOnlyList<CashFlowIntervalOption> IntervalOptions { get; }

    public IReadOnlyList<CashFlowDirectionOption> DirectionOptions { get; }

    public IReadOnlyList<HorizonOption> HorizonOptions { get; }

    public bool IsEmpty => !IsLoading && !HasData && string.IsNullOrEmpty(ErrorMessage);

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        OnPropertyChanged(nameof(IsEmpty));
        ErrorMessage = "";
        try
        {
            await RefreshAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cash-flow plan");
            ErrorMessage = _localizer["CashFlow.Error.Load"];
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private async Task AddFlowAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(NewFlowName)
            || NewFlowAmount <= 0m
            || NewFlowAnchorDay is < 1 or > 31
            || NewFlowDirection is not { } direction
            || NewFlowInterval is not { } interval)
        {
            ErrorMessage = _localizer["CashFlow.Error.Validation"];
            return;
        }

        try
        {
            await _repository.AddAsync(
                new RecurringFlow
                {
                    Id = Guid.NewGuid(),
                    Name = NewFlowName.Trim(),
                    Direction = direction.Value,
                    IntervalMonths = interval.Months,
                    AnchorDayOfMonth = NewFlowAnchorDay,
                    TypicalAmount = NewFlowAmount,
                    AccrualOffsetMonths = NewFlowAccrualOffsetMonths,
                    Currency = "PLN",
                    IsActive = true,
                    Source = FlowSource.Manual,
                    CreatedAt = DateTime.UtcNow,
                },
                ct).ConfigureAwait(true);

            NewFlowName = "";
            NewFlowAmount = 0m;
            NewFlowAnchorDay = 1;
            NewFlowAccrualOffsetMonths = 0;
            ErrorMessage = "";

            await RefreshAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add recurring flow");
            ErrorMessage = _localizer["CashFlow.Error.Add"];
        }
    }

    [RelayCommand]
    private async Task ExplainAsync(CancellationToken ct)
    {
        if (_projection is not { } projection || IsExplaining)
        {
            return;
        }

        IsExplaining = true;
        try
        {
            var explanation = await _explainer.ExplainAsync(projection, ct).ConfigureAwait(true);
            Narrative = explanation.Narrative;
            NarrativeIsAi = explanation.GeneratedByAi;
            HasNarrative = !string.IsNullOrWhiteSpace(explanation.Narrative);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to explain cash-flow projection");
            ErrorMessage = _localizer["CashFlow.Error.Explain"];
        }
        finally
        {
            IsExplaining = false;
        }
    }

    partial void OnSelectedHorizonChanged(HorizonOption? value)
    {
        if (value is not null && HasData)
        {
            Project();
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        _today = DateOnly.FromDateTime(DateTime.Today);
        _flows = await _repository.GetAllAsync(ct).ConfigureAwait(true);
        _openingBalance = await _balanceQuery.GetBalanceAsOfAsync(_today, accountId: null, ct).ConfigureAwait(true);
        var candidates = await _detector.DetectAsync(ct).ConfigureAwait(true);
        var gaps = await _continuityChecker.FindGapsAsync(ct).ConfigureAwait(true);

        var known = _flows
            .Where(f => f.MatchMerchant is not null)
            .Select(f => f.MatchMerchant!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Flows.Clear();
        foreach (var flow in _flows)
        {
            Flows.Add(new RecurringFlowViewModel(flow, IntervalOptions, _localizer, SaveFlowAsync, DeleteFlowAsync));
        }

        Suggestions.Clear();
        foreach (var candidate in candidates)
        {
            if (candidate.MatchMerchant is not null && known.Contains(candidate.MatchMerchant))
            {
                continue;
            }

            Suggestions.Add(new FlowCandidateViewModel(candidate, _localizer, ConfirmCandidateAsync));
        }

        StatementGaps.Clear();
        foreach (var gap in gaps)
        {
            StatementGaps.Add(_localizer.Format(
                "CashFlow.Gap.Range",
                CashFlowDisplay.Date(gap.From),
                CashFlowDisplay.Date(gap.To)));
        }

        HasSuggestions = Suggestions.Count > 0;
        HasGaps = StatementGaps.Count > 0;

        Project();
    }

    private void Project()
    {
        var horizon = SelectedHorizon?.Days ?? 90;
        var projection = _engine.Project(_flows, _openingBalance, _today, horizon);
        _projection = projection;

        // The previous narration describes a stale projection; require a fresh explanation.
        Narrative = "";
        HasNarrative = false;
        NarrativeIsAi = false;

        Timeline.Clear();
        foreach (var e in projection.Events)
        {
            Timeline.Add(new CashFlowEventViewModel(e, _localizer));
        }

        OpeningBalanceText = CashFlowDisplay.Money(projection.OpeningBalance);
        ClosingBalanceText = CashFlowDisplay.Money(projection.ClosingBalance);
        LowestBalanceText = CashFlowDisplay.Money(projection.LowestBalance);
        LowestBalanceDateText = projection.LowestBalanceDate is { } date
            ? CashFlowDisplay.Date(date)
            : "";
        HasTightWindow = projection.HasTightWindow;

        BalanceSeries =
        [
            new LineSeries<decimal>
            {
                Name = _localizer["CashFlow.Chart.Balance"],
                Values = projection.Events.Select(e => e.BalanceAfter).ToArray(),
                Stroke = new SolidColorPaint(_balanceStroke, 2),
                Fill = new SolidColorPaint(_balanceFill),
                GeometryStroke = null,
                GeometryFill = null,
                GeometrySize = 0,
                LineSmoothness = 0,
            },
        ];
        BalanceXAxes =
        [
            new Axis
            {
                Labels = projection.Events.Select(e => CashFlowDisplay.ShortDate(e.Date)).ToArray(),
                LabelsRotation = 0,
                MinStep = 1,
            },
        ];

        HasData = _flows.Count > 0;
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task SaveFlowAsync(RecurringFlowViewModel row)
    {
        if (row.IsBusy)
        {
            return;
        }

        row.IsBusy = true;
        try
        {
            await _repository.UpdateAsync(
                new RecurringFlow
                {
                    Id = row.Id,
                    Name = row.Name.Trim(),
                    Direction = row.Direction,
                    IntervalMonths = row.IntervalMonths,
                    AnchorDayOfMonth = row.AnchorDayOfMonth,
                    TypicalAmount = row.Amount,
                    AccrualOffsetMonths = row.AccrualOffsetMonths,
                    Currency = "PLN",
                    IsActive = row.IsActive,
                    Source = FlowSource.Manual,
                    CreatedAt = DateTime.UtcNow,
                },
                CancellationToken.None).ConfigureAwait(true);

            await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save recurring flow {FlowId}", row.Id);
            row.IsBusy = false;
            ErrorMessage = _localizer["CashFlow.Error.Save"];
        }
    }

    private async Task DeleteFlowAsync(RecurringFlowViewModel row)
    {
        if (row.IsBusy)
        {
            return;
        }

        row.IsBusy = true;
        try
        {
            await _repository.DeleteAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete recurring flow {FlowId}", row.Id);
            row.IsBusy = false;
            ErrorMessage = _localizer["CashFlow.Error.Delete"];
        }
    }

    private async Task ConfirmCandidateAsync(FlowCandidateViewModel row)
    {
        if (row.IsBusy)
        {
            return;
        }

        row.IsBusy = true;
        try
        {
            var c = row.Candidate;
            await _repository.AddAsync(
                new RecurringFlow
                {
                    Id = Guid.NewGuid(),
                    Name = c.Name,
                    Direction = c.Direction,
                    MatchMerchant = c.MatchMerchant,
                    MatchCategoryId = c.MatchCategoryId,
                    IntervalMonths = c.IntervalMonths,
                    AnchorDayOfMonth = c.AnchorDayOfMonth,
                    AnchorMonth = c.AnchorMonth,
                    TypicalAmount = c.TypicalAmount,
                    AmountStdDev = c.AmountStdDev,
                    AccrualOffsetMonths = 0,
                    Currency = "PLN",
                    IsActive = true,
                    Source = FlowSource.Detected,
                    CreatedAt = DateTime.UtcNow,
                },
                CancellationToken.None).ConfigureAwait(true);

            await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm flow candidate {Name}", row.Name);
            row.IsBusy = false;
            ErrorMessage = _localizer["CashFlow.Error.Confirm"];
        }
    }
}

/// <summary>A cadence choice for the flow form: months between occurrences plus its localized caption.</summary>
public sealed record CashFlowIntervalOption(int Months, string Label);

/// <summary>A projection-window choice: the horizon length in days plus its localized caption.</summary>
public sealed record HorizonOption(int Days, string Label);

/// <summary>A direction choice for the new-flow form: the enum value plus its localized caption.</summary>
public sealed record CashFlowDirectionOption(FlowDirection Value, string Label);
