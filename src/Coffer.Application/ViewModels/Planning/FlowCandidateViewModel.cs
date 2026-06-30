using Coffer.Application.Localization;
using Coffer.Core.Domain;
using Coffer.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coffer.Application.ViewModels.Planning;

/// <summary>
/// A detected recurring-flow suggestion the owner can confirm into a persisted, active flow. Detection
/// only proposes (the engine never writes); confirming hands the underlying <see cref="RecurringFlowCandidate"/>
/// back to the planning VM, which persists it and re-projects.
/// </summary>
public sealed partial class FlowCandidateViewModel : ObservableObject
{
    private readonly Func<FlowCandidateViewModel, Task> _confirm;

    [ObservableProperty]
    private bool _isBusy;

    public FlowCandidateViewModel(
        RecurringFlowCandidate candidate,
        ILocalizer localizer,
        Func<FlowCandidateViewModel, Task> confirm)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(confirm);

        Candidate = candidate;
        Name = candidate.Name;
        Direction = candidate.Direction;
        DirectionText = localizer[CashFlowDisplay.DirectionKey(candidate.Direction)];
        DirectionColor = CashFlowDisplay.DirectionColor(candidate.Direction);
        AmountText = CashFlowDisplay.Money(candidate.TypicalAmount);
        IntervalText = localizer[CashFlowDisplay.IntervalKey(candidate.IntervalMonths)];
        DayText = localizer.Format("CashFlow.Candidate.Day", candidate.AnchorDayOfMonth);
        _confirm = confirm;
    }

    public RecurringFlowCandidate Candidate { get; }

    public string Name { get; }

    public FlowDirection Direction { get; }

    public string DirectionText { get; }

    public string DirectionColor { get; }

    public string AmountText { get; }

    public string IntervalText { get; }

    public string DayText { get; }

    [RelayCommand]
    private Task ConfirmAsync() => _confirm(this);
}
