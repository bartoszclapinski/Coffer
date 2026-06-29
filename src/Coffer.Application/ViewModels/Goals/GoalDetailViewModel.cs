using System.Collections.ObjectModel;
using System.Globalization;
using Coffer.Application.Localization;
using Coffer.Core.Domain;
using Coffer.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Coffer.Application.ViewModels.Goals;

/// <summary>
/// One goal on the Doradca page, serving both the list row and the detail panel. The engine's
/// deterministic <see cref="GoalFeasibilityResult"/> supplies every figure; this VM only formats
/// them for display and drives the live simulator. Moving <see cref="MonthlySavingInput"/> re-runs
/// the (pure, free) <see cref="IGoalFeasibilityEngine.Simulate"/> and redraws the 12-month
/// projection — the engine calculates, the VM never invents a number. Archive and contribution
/// mutations are delegated to the parent page through callbacks.
/// </summary>
public sealed partial class GoalDetailViewModel : ObservableObject
{
    private static readonly CultureInfo _polish = CultureInfo.GetCultureInfo("pl-PL");
    private static readonly SKColor _projectionStroke = SKColor.Parse("#1D4ED8");
    private static readonly SKColor _projectionFill = SKColor.Parse("#1D4ED8").WithAlpha(40);
    private static readonly SKColor _targetStroke = SKColor.Parse("#8E8E93");

    private const int _projectionMonths = 12;

    private readonly Goal _goal;
    private readonly FinancialContext _context;
    private readonly IGoalFeasibilityEngine _engine;
    private readonly ILocalizer _localizer;
    private readonly decimal _savedAmount;
    private readonly decimal _effectiveTarget;
    private readonly Func<GoalDetailViewModel, Task> _onArchive;
    private readonly Func<GoalDetailViewModel, decimal, DateOnly, Task> _onAddContribution;

    [ObservableProperty]
    private decimal _monthlySavingInput;

    [ObservableProperty]
    private string _simulatedProjectedDateText = "";

    [ObservableProperty]
    private string _simulatedStatusText = "";

    [ObservableProperty]
    private string _simulatedStatusColor = "";

    [ObservableProperty]
    private ISeries[] _projectionSeries = [];

    [ObservableProperty]
    private Axis[] _projectionXAxes = [];

    [ObservableProperty]
    private decimal _contributionAmount;

    [ObservableProperty]
    private bool _isBusy;

    public GoalDetailViewModel(
        Goal goal,
        GoalFeasibilityResult result,
        FinancialContext context,
        IGoalFeasibilityEngine engine,
        ILocalizer localizer,
        Func<GoalDetailViewModel, Task> onArchive,
        Func<GoalDetailViewModel, decimal, DateOnly, Task> onAddContribution)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(onArchive);
        ArgumentNullException.ThrowIfNull(onAddContribution);

        _goal = goal;
        _context = context;
        _engine = engine;
        _localizer = localizer;
        _onArchive = onArchive;
        _onAddContribution = onAddContribution;

        Id = goal.Id;
        Name = goal.Name;
        TypeText = localizer[GoalDisplay.TypeKey(goal.Type)];
        PriorityText = localizer[GoalDisplay.PriorityKey(goal.Priority)];

        _savedAmount = goal.Contributions.Sum(c => c.Amount);
        _effectiveTarget = result.EffectiveTarget > 0m ? result.EffectiveTarget : goal.TargetAmount;

        StatusText = localizer[GoalDisplay.StatusKey(result.Status)];
        StatusColor = GoalDisplay.StatusToColor(result.Status);
        TargetText = GoalDisplay.Money(_effectiveTarget);
        SavedText = GoalDisplay.Money(_savedAmount);
        RemainingText = GoalDisplay.Money(Math.Max(0m, _effectiveTarget - _savedAmount));
        TargetDateText = goal.TargetDate.ToString("d MMM yyyy", _polish);
        ProjectedDateText = FormatProjectedDate(result.ProjectedDate);
        RequiredMonthlyText = GoalDisplay.Money(result.RequiredMonthlySaving) + localizer["Goal.PerMonthSuffix"];
        CurrentMonthlyText = GoalDisplay.Money(result.CurrentMonthlySaving) + localizer["Goal.PerMonthSuffix"];
        ConfidenceText = (result.ConfidenceScore * 100m).ToString("0", _polish) + "%";

        Scenarios = new ObservableCollection<GoalScenarioViewModel>(
            result.AlternativeScenarios.Select(s => new GoalScenarioViewModel(s, localizer)));
        Risks = new ObservableCollection<string>(
            result.Risks.Select(r => GoalDisplay.RiskKey(r.Code) is { } key ? localizer[key] : r.Description));

        MonthlySavingInput = result.RequiredMonthlySaving > 0m
            ? decimal.Round(result.RequiredMonthlySaving, 2)
            : decimal.Round(result.CurrentMonthlySaving, 2);
    }

    public Guid Id { get; }

    public string Name { get; }

    public string TypeText { get; }

    public string PriorityText { get; }

    public string StatusText { get; }

    public string StatusColor { get; }

    public string TargetText { get; }

    public string SavedText { get; }

    public string RemainingText { get; }

    public string TargetDateText { get; }

    public string ProjectedDateText { get; }

    public string RequiredMonthlyText { get; }

    public string CurrentMonthlyText { get; }

    public string ConfidenceText { get; }

    public ObservableCollection<GoalScenarioViewModel> Scenarios { get; }

    public ObservableCollection<string> Risks { get; }

    public bool HasRisks => Risks.Count > 0;

    [RelayCommand]
    private Task ArchiveAsync() => _onArchive(this);

    [RelayCommand]
    private Task AddContributionAsync()
    {
        if (ContributionAmount <= 0m)
        {
            return Task.CompletedTask;
        }

        return _onAddContribution(this, ContributionAmount, _context.Today);
    }

    partial void OnMonthlySavingInputChanged(decimal value) => Recompute();

    private string FormatProjectedDate(DateOnly date) =>
        GoalDisplay.IsUnreachable(date)
            ? _localizer["Goal.ProjectedDate.Unreachable"]
            : GoalDisplay.FormatProjectedDate(date);

    private void Recompute()
    {
        var scenario = _engine.Simulate(_goal, _context, MonthlySavingInput);
        SimulatedProjectedDateText = FormatProjectedDate(scenario.ProjectedDate);
        SimulatedStatusText = _localizer[GoalDisplay.StatusKey(scenario.Status)];
        SimulatedStatusColor = GoalDisplay.StatusToColor(scenario.Status);
        BuildProjection();
    }

    private void BuildProjection()
    {
        var cumulative = new decimal[_projectionMonths + 1];
        var target = new decimal[_projectionMonths + 1];
        var labels = new string[_projectionMonths + 1];

        for (var month = 0; month <= _projectionMonths; month++)
        {
            cumulative[month] = Math.Min(_effectiveTarget, _savedAmount + (MonthlySavingInput * month));
            target[month] = _effectiveTarget;
            labels[month] = _context.Today.AddMonths(month).ToString("MMM yy", _polish);
        }

        ProjectionSeries =
        [
            new LineSeries<decimal>
            {
                Name = _localizer["Goal.Chart.Savings"],
                Values = cumulative,
                Stroke = new SolidColorPaint(_projectionStroke, 2),
                Fill = new SolidColorPaint(_projectionFill),
                GeometryStroke = null,
                GeometryFill = null,
                GeometrySize = 0,
                LineSmoothness = 0,
            },
            new LineSeries<decimal>
            {
                Name = _localizer["Goal.Chart.Target"],
                Values = target,
                Stroke = new SolidColorPaint(_targetStroke, 1),
                Fill = null,
                GeometryStroke = null,
                GeometryFill = null,
                GeometrySize = 0,
            },
        ];
        ProjectionXAxes =
        [
            new Axis
            {
                Labels = labels,
                LabelsRotation = 0,
                MinStep = 1,
            },
        ];
    }
}
