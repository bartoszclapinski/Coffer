using System.Globalization;
using Coffer.Core.Anomalies;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coffer.Application.ViewModels.Alerts;

/// <summary>
/// One card on the Alerty page. Wraps a read-only <see cref="AlertListItem"/> and exposes the
/// two lifecycle actions (acknowledge keeps it, dismiss suppresses it for good) as commands the
/// page view-model fulfils. Display strings are pre-formatted Polish so the view stays markup-only.
/// </summary>
public sealed partial class AlertRowViewModel : ObservableObject
{
    private static readonly CultureInfo _polish = CultureInfo.GetCultureInfo("pl-PL");

    private readonly Func<AlertRowViewModel, Task> _onAcknowledge;
    private readonly Func<AlertRowViewModel, Task> _onDismiss;

    [ObservableProperty]
    private bool _isBusy;

    public AlertRowViewModel(
        AlertListItem item,
        Func<AlertRowViewModel, Task> onAcknowledge,
        Func<AlertRowViewModel, Task> onDismiss)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(onAcknowledge);
        ArgumentNullException.ThrowIfNull(onDismiss);

        Id = item.Id;
        Type = item.Type;
        Title = item.Title;
        Description = item.Description;
        AmountText = item.RelatedAmount is { } amount ? amount.ToString("N2", _polish) + " zł" : "";
        PeriodText = FormatPeriod(item.PeriodFrom, item.PeriodTo);
        _onAcknowledge = onAcknowledge;
        _onDismiss = onDismiss;
    }

    public Guid Id { get; }

    public AnomalyType Type { get; }

    public string Title { get; }

    public string Description { get; }

    public string AmountText { get; }

    public bool HasAmount => AmountText.Length > 0;

    public string PeriodText { get; }

    [RelayCommand]
    private Task AcknowledgeAsync() => _onAcknowledge(this);

    [RelayCommand]
    private Task DismissAsync() => _onDismiss(this);

    private static string FormatPeriod(DateOnly from, DateOnly to)
    {
        if (from == to)
        {
            return from.ToString("d MMMM yyyy", _polish);
        }

        return $"{from.ToString("d MMM", _polish)} – {to.ToString("d MMM yyyy", _polish)}";
    }
}
