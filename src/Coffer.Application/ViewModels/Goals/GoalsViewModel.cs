using System.Collections.ObjectModel;
using Coffer.Application.Localization;
using Coffer.Core.Domain;
using Coffer.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Goals;

/// <summary>
/// View-model behind the Doradca page. Opening it builds the deterministic
/// <see cref="FinancialContext"/> from the transaction history, loads the active goals with their
/// contributions, and runs <see cref="IGoalFeasibilityEngine.EvaluateAll"/> so every goal is scored
/// against the same free cash (cross-goal pull included). Creating, archiving, or contributing
/// mutates through <see cref="IGoalService"/> and re-evaluates. No AI here — the engine calculates;
/// the 14-C report only explains.
/// </summary>
public sealed partial class GoalsViewModel : ObservableObject
{
    private readonly IGoalsQuery _query;
    private readonly IGoalService _service;
    private readonly IFinancialContextBuilder _contextBuilder;
    private readonly IGoalFeasibilityEngine _engine;
    private readonly IAdvisorReportQuery _reportQuery;
    private readonly ILocalizer _localizer;
    private readonly ILogger<GoalsViewModel> _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasGoals;

    [ObservableProperty]
    private bool _hasSuggestions;

    [ObservableProperty]
    private bool _suggestionsAreEngineOnly;

    [ObservableProperty]
    private GoalDetailViewModel? _selectedGoal;

    [ObservableProperty]
    private string _newGoalName = "";

    [ObservableProperty]
    private decimal _newGoalTargetAmount;

    [ObservableProperty]
    private DateTimeOffset? _newGoalTargetDate = DateTimeOffset.Now.AddMonths(6);

    [ObservableProperty]
    private GoalTypeOption? _newGoalType;

    [ObservableProperty]
    private PriorityOption? _newGoalPriority;

    public GoalsViewModel(
        IGoalsQuery query,
        IGoalService service,
        IFinancialContextBuilder contextBuilder,
        IGoalFeasibilityEngine engine,
        IAdvisorReportQuery reportQuery,
        ILocalizer localizer,
        ILogger<GoalsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(contextBuilder);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(reportQuery);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _query = query;
        _service = service;
        _contextBuilder = contextBuilder;
        _engine = engine;
        _reportQuery = reportQuery;
        _localizer = localizer;
        _logger = logger;

        GoalTypeOptions = Enum.GetValues<GoalType>()
            .Select(t => new GoalTypeOption(t, _localizer[GoalDisplay.TypeKey(t)]))
            .ToArray();
        PriorityOptions = Enum.GetValues<Priority>()
            .Select(p => new PriorityOption(p, _localizer[GoalDisplay.PriorityKey(p)]))
            .ToArray();

        _newGoalType = GoalTypeOptions[0];
        _newGoalPriority = PriorityOptions.First(p => p.Value == Priority.Medium);
    }

    public ObservableCollection<GoalDetailViewModel> Goals { get; } = [];

    public ObservableCollection<AdvisorSuggestionViewModel> Suggestions { get; } = [];

    public IReadOnlyList<GoalTypeOption> GoalTypeOptions { get; }

    public IReadOnlyList<PriorityOption> PriorityOptions { get; }

    public bool IsEmpty => !IsLoading && !HasGoals && string.IsNullOrEmpty(ErrorMessage);

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
            _logger.LogError(ex, "Failed to load goals");
            ErrorMessage = _localizer["Goals.Error.Load"];
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private async Task CreateGoalAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(NewGoalName)
            || NewGoalTargetAmount <= 0m
            || NewGoalTargetDate is not { } date
            || NewGoalType is not { } type
            || NewGoalPriority is not { } priority)
        {
            ErrorMessage = _localizer["Goals.Error.Validation"];
            return;
        }

        try
        {
            await _service.CreateAsync(
                new NewGoal(
                    NewGoalName.Trim(),
                    type.Value,
                    NewGoalTargetAmount,
                    "PLN",
                    DateOnly.FromDateTime(date.Date),
                    priority.Value,
                    null),
                ct).ConfigureAwait(true);

            NewGoalName = "";
            NewGoalTargetAmount = 0m;
            NewGoalTargetDate = DateTimeOffset.Now.AddMonths(6);
            ErrorMessage = "";

            await RefreshAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create goal");
            ErrorMessage = _localizer["Goals.Error.Create"];
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var context = await _contextBuilder.BuildAsync(today, ct).ConfigureAwait(true);
        var goals = await _query.GetActiveAsync(ct).ConfigureAwait(true);
        var results = _engine.EvaluateAll(goals, context).ToDictionary(r => r.GoalId);

        var previousId = SelectedGoal?.Id;

        Goals.Clear();
        foreach (var goal in goals)
        {
            if (!results.TryGetValue(goal.Id, out var result))
            {
                continue;
            }

            Goals.Add(new GoalDetailViewModel(goal, result, context, _engine, _localizer, ArchiveAsync, AddContributionAsync));
        }

        HasGoals = Goals.Count > 0;
        SelectedGoal = Goals.FirstOrDefault(g => g.Id == previousId) ?? Goals.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));

        await RefreshSuggestionsAsync(ct).ConfigureAwait(true);
    }

    private async Task RefreshSuggestionsAsync(CancellationToken ct)
    {
        Suggestions.Clear();
        var report = await _reportQuery.GetLatestAsync(ct).ConfigureAwait(true);
        if (report is not null)
        {
            foreach (var entry in report.Entries.Where(e => e.Kind == AdvisorEntryKind.Suggestion))
            {
                Suggestions.Add(new AdvisorSuggestionViewModel(entry));
            }
        }

        HasSuggestions = Suggestions.Count > 0;
        SuggestionsAreEngineOnly = report is { GeneratedByAi: false } && HasSuggestions;
    }

    private async Task ArchiveAsync(GoalDetailViewModel goal)
    {
        if (goal.IsBusy)
        {
            return;
        }

        goal.IsBusy = true;
        try
        {
            await _service.ArchiveAsync(goal.Id, CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive goal {GoalId}", goal.Id);
            goal.IsBusy = false;
            ErrorMessage = _localizer["Goals.Error.Archive"];
        }
    }

    private async Task AddContributionAsync(GoalDetailViewModel goal, decimal amount, DateOnly date)
    {
        if (goal.IsBusy)
        {
            return;
        }

        goal.IsBusy = true;
        try
        {
            await _service.AddContributionAsync(goal.Id, amount, date, CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add contribution to goal {GoalId}", goal.Id);
            goal.IsBusy = false;
            ErrorMessage = _localizer["Goals.Error.Contribution"];
        }
    }
}

/// <summary>A goal-type choice for the new-goal form: the enum value plus its localized caption.</summary>
public sealed record GoalTypeOption(GoalType Value, string Label);

/// <summary>A priority choice for the new-goal form: the enum value plus its localized caption.</summary>
public sealed record PriorityOption(Priority Value, string Label);
