using Coffer.Application.Localization;
using Coffer.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coffer.Application.ViewModels.Planning;

/// <summary>
/// An editable row for one persisted <see cref="RecurringFlow"/>. The owner adjusts the amount, the
/// anchor day, the cadence and — crucially — the accrual offset (their domain knowledge), then saves
/// or deletes. Mutations are delegated back to the planning VM so persistence and re-projection stay
/// in one place.
/// </summary>
public sealed partial class RecurringFlowViewModel : ObservableObject
{
    private readonly Func<RecurringFlowViewModel, Task> _save;
    private readonly Func<RecurringFlowViewModel, Task> _delete;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private decimal _amount;

    [ObservableProperty]
    private int _anchorDayOfMonth;

    [ObservableProperty]
    private CashFlowIntervalOption _selectedInterval;

    [ObservableProperty]
    private int _accrualOffsetMonths;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isBusy;

    public RecurringFlowViewModel(
        RecurringFlow flow,
        IReadOnlyList<CashFlowIntervalOption> intervalOptions,
        ILocalizer localizer,
        Func<RecurringFlowViewModel, Task> save,
        Func<RecurringFlowViewModel, Task> delete)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(intervalOptions);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(delete);

        Id = flow.Id;
        Direction = flow.Direction;
        DirectionText = localizer[CashFlowDisplay.DirectionKey(flow.Direction)];
        DirectionColor = CashFlowDisplay.DirectionColor(flow.Direction);
        IntervalOptions = intervalOptions;

        _name = flow.Name;
        _amount = flow.TypicalAmount;
        _anchorDayOfMonth = flow.AnchorDayOfMonth;
        _accrualOffsetMonths = flow.AccrualOffsetMonths;
        _isActive = flow.IsActive;
        _selectedInterval = intervalOptions.FirstOrDefault(o => o.Months == flow.IntervalMonths)
            ?? intervalOptions[0];

        _save = save;
        _delete = delete;
    }

    public Guid Id { get; }

    public FlowDirection Direction { get; }

    public string DirectionText { get; }

    public string DirectionColor { get; }

    public IReadOnlyList<CashFlowIntervalOption> IntervalOptions { get; }

    public int IntervalMonths => SelectedInterval.Months;

    [RelayCommand]
    private Task SaveAsync() => _save(this);

    [RelayCommand]
    private Task DeleteAsync() => _delete(this);
}
