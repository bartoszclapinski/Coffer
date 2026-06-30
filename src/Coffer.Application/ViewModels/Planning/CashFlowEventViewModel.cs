using Coffer.Application.Localization;
using Coffer.Core.Domain;
using Coffer.Core.Planning;

namespace Coffer.Application.ViewModels.Planning;

/// <summary>
/// One dated row in the forward timeline: when money moves, which flow, the signed amount, the
/// running balance afterwards, the accrual period the cost belongs to, and whether the balance is
/// tight at this point. Read-only — it only shapes a <see cref="CashFlowEvent"/> for the view.
/// </summary>
public sealed class CashFlowEventViewModel
{
    public CashFlowEventViewModel(CashFlowEvent e, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(localizer);

        Name = e.Name;
        Direction = e.Direction;
        IsTight = e.IsTight;
        DateText = CashFlowDisplay.Date(e.Date);
        AmountText = CashFlowDisplay.SignedMoney(e.Direction, Math.Abs(e.Amount));
        BalanceText = CashFlowDisplay.Money(e.BalanceAfter);
        DirectionText = localizer[CashFlowDisplay.DirectionKey(e.Direction)];
        DirectionColor = CashFlowDisplay.DirectionColor(e.Direction);
        AccrualText = localizer.Format("CashFlow.Timeline.Accrual", CashFlowDisplay.AccrualPeriod(e.AccrualPeriod));
    }

    public string Name { get; }

    public FlowDirection Direction { get; }

    public bool IsTight { get; }

    public string DateText { get; }

    public string AmountText { get; }

    public string BalanceText { get; }

    public string DirectionText { get; }

    public string DirectionColor { get; }

    public string AccrualText { get; }
}
