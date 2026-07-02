using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coffer.Application.ViewModels.Settings;

/// <summary>
/// One row in the Settings balance-anchor editor: an account plus its editable "real balance as of date"
/// (18-A). Saving persists the amount + date as the account's anchor; clearing removes it so the balance
/// reverts to a relative running sum. The actual persistence is delegated to the parent
/// <see cref="SettingsViewModel"/> (which owns <c>IAccountService</c>); this row only holds the edit state.
/// </summary>
public sealed partial class AccountAnchorRowViewModel : ObservableObject
{
    private readonly Func<AccountAnchorRowViewModel, Task> _save;
    private readonly Func<AccountAnchorRowViewModel, Task> _clear;

    [ObservableProperty]
    private decimal _anchorBalance;

    [ObservableProperty]
    private DateTimeOffset? _anchorDate;

    [ObservableProperty]
    private bool _hasAnchor;

    [ObservableProperty]
    private bool _isBusy;

    public AccountAnchorRowViewModel(
        Guid id,
        string name,
        string bankCode,
        DateOnly? anchorDate,
        decimal? anchorBalance,
        Func<AccountAnchorRowViewModel, Task> save,
        Func<AccountAnchorRowViewModel, Task> clear)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(clear);

        Id = id;
        Name = name;
        BankCode = bankCode;
        _save = save;
        _clear = clear;

        _hasAnchor = anchorDate is not null && anchorBalance is not null;
        _anchorBalance = anchorBalance ?? 0m;
        _anchorDate = anchorDate is { } date
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string BankCode { get; }

    [RelayCommand]
    private Task SaveAnchor() => _save(this);

    [RelayCommand]
    private Task ClearAnchor() => _clear(this);
}
