using System.Collections.ObjectModel;
using Coffer.Application.Localization;
using Coffer.Core.Accounts;
using Coffer.Core.Domain;
using Coffer.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Planning;

/// <summary>
/// View-model behind the dedicated "Can I afford?" page. The owner picks an account (or all accounts),
/// enters an amount and an optional date, and gets a grounded verdict from the deterministic
/// <see cref="AffordabilityEngine"/>: afford/not, the projected low point before the next inflow, the
/// headroom over the safety floor, and the recurring payment that pushes the balance under. When the
/// chosen account has no balance anchor the answer is flagged <em>relative</em>; when a statement gap
/// sits in the window it is flagged <em>uncertain</em>. Every number comes from the engine — this VM
/// only assembles its inputs and formats its output (the Sprint-14 "engine calculates" rule); it makes
/// zero AI calls, mirroring <c>CanIAffordTool</c>.
/// </summary>
public sealed partial class AffordabilityViewModel : ObservableObject
{
    private const string Currency = "PLN";

    private readonly AffordabilityEngine _engine;
    private readonly IRunningBalanceQuery _balanceQuery;
    private readonly IVariableBurnQuery _burnQuery;
    private readonly IBalanceTrustQuery _trustQuery;
    private readonly IPlanningSettings _planningSettings;
    private readonly IRecurringFlowRepository _flowRepository;
    private readonly IAccountService _accountService;
    private readonly ILocalizer _localizer;
    private readonly ILogger<AffordabilityViewModel> _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private AccountOption? _selectedAccount;

    [ObservableProperty]
    private decimal _amount;

    [ObservableProperty]
    private DateTimeOffset? _spendDate = DateTimeOffset.Now;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _canAfford;

    [ObservableProperty]
    private string _verdictText = "";

    [ObservableProperty]
    private string _amountText = "";

    [ObservableProperty]
    private string _spendDateText = "";

    [ObservableProperty]
    private string _openingBalanceText = "";

    [ObservableProperty]
    private string _lowestBalanceText = "";

    [ObservableProperty]
    private string _lowestBalanceDateText = "";

    [ObservableProperty]
    private string _headroomText = "";

    [ObservableProperty]
    private string _safetyFloorText = "";

    [ObservableProperty]
    private string _dailyBurnText = "";

    [ObservableProperty]
    private string _nextInflowText = "";

    [ObservableProperty]
    private bool _hasDriver;

    [ObservableProperty]
    private string _driverText = "";

    [ObservableProperty]
    private bool _isUncertain;

    [ObservableProperty]
    private string _uncertaintyText = "";

    [ObservableProperty]
    private bool _isRelative;

    public AffordabilityViewModel(
        AffordabilityEngine engine,
        IRunningBalanceQuery balanceQuery,
        IVariableBurnQuery burnQuery,
        IBalanceTrustQuery trustQuery,
        IPlanningSettings planningSettings,
        IRecurringFlowRepository flowRepository,
        IAccountService accountService,
        ILocalizer localizer,
        ILogger<AffordabilityViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(balanceQuery);
        ArgumentNullException.ThrowIfNull(burnQuery);
        ArgumentNullException.ThrowIfNull(trustQuery);
        ArgumentNullException.ThrowIfNull(planningSettings);
        ArgumentNullException.ThrowIfNull(flowRepository);
        ArgumentNullException.ThrowIfNull(accountService);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _engine = engine;
        _balanceQuery = balanceQuery;
        _burnQuery = burnQuery;
        _trustQuery = trustQuery;
        _planningSettings = planningSettings;
        _flowRepository = flowRepository;
        _accountService = accountService;
        _localizer = localizer;
        _logger = logger;
    }

    public ObservableCollection<AccountOption> Accounts { get; } = [];

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var accounts = await _accountService.GetAllWithAnchorsAsync(ct).ConfigureAwait(true);

            var previous = SelectedAccount?.Id;
            Accounts.Clear();
            Accounts.Add(new AccountOption(null, _localizer["Affordability.AllAccounts"], AnchorDate: null));
            foreach (var a in accounts)
            {
                Accounts.Add(new AccountOption(a.Id, a.Name, a.AnchorDate));
            }

            SelectedAccount = Accounts.FirstOrDefault(o => o.Id == previous) ?? Accounts[0];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load accounts for the affordability page");
            ErrorMessage = _localizer["Affordability.Error.Load"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CheckAsync(CancellationToken ct)
    {
        if (Amount <= 0m)
        {
            ErrorMessage = _localizer["Affordability.Error.Amount"];
            HasResult = false;
            return;
        }

        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var spendDate = SpendDate is { } offset
                ? DateOnly.FromDateTime(offset.Date)
                : DateOnly.FromDateTime(DateTime.Today);
            var account = SelectedAccount;
            var accountId = account?.Id;

            var flows = (await _flowRepository.GetActiveAsync(ct).ConfigureAwait(true))
                .Where(f => f.Currency == Currency)
                .ToList();
            var opening = await _balanceQuery.GetBalanceAsOfAsync(spendDate, accountId, ct).ConfigureAwait(true);
            var dailyBurn = await _burnQuery.GetDailyBurnAsync(accountId, spendDate, ct).ConfigureAwait(true);
            var safetyFloor = await _planningSettings.GetSafetyFloorPlnAsync(ct).ConfigureAwait(true);

            bool isRelative;
            BalanceTrust trust;
            if (accountId is Guid id)
            {
                // The anchor came back with the account list, so there is no need to re-read it here.
                isRelative = account!.AnchorDate is null;
                trust = await _trustQuery.CheckAsync(id, spendDate, ct).ConfigureAwait(true);
            }
            else
            {
                // A cross-account blend has no single anchor and no per-account continuity signal.
                isRelative = true;
                trust = new BalanceTrust(IsTrustworthy: true, WindowFrom: spendDate, Gaps: Array.Empty<StatementGap>());
            }

            var verdict = _engine.Assess(Amount, spendDate, opening, flows, dailyBurn, safetyFloor, trust, isRelative);
            Present(verdict);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assess affordability");
            ErrorMessage = _localizer["Affordability.Error.Check"];
            HasResult = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Present(AffordabilityVerdict verdict)
    {
        CanAfford = verdict.CanAfford;
        VerdictText = verdict.CanAfford
            ? _localizer["Affordability.Result.Yes"]
            : _localizer["Affordability.Result.No"];
        AmountText = CashFlowDisplay.Money(verdict.SpendAmount);
        SpendDateText = CashFlowDisplay.Date(verdict.SpendDate);
        OpeningBalanceText = CashFlowDisplay.Money(verdict.OpeningBalance);
        LowestBalanceText = CashFlowDisplay.Money(verdict.LowestBalance);
        LowestBalanceDateText = CashFlowDisplay.Date(verdict.LowestBalanceDate);
        HeadroomText = CashFlowDisplay.Money(verdict.Headroom);
        SafetyFloorText = CashFlowDisplay.Money(verdict.SafetyFloor);
        DailyBurnText = CashFlowDisplay.Money(verdict.DailyBurn);
        NextInflowText = verdict.NextInflowDate is { } inflow
            ? CashFlowDisplay.Date(inflow)
            : _localizer["Affordability.NoInflow"];

        HasDriver = verdict.Driver is not null;
        DriverText = verdict.Driver is { } d
            ? _localizer.Format(
                "Affordability.Driver.Detail",
                d.Name,
                CashFlowDisplay.Date(d.Date),
                CashFlowDisplay.Money(Math.Abs(d.Amount)))
            : "";

        IsUncertain = verdict.IsUncertain;
        UncertaintyText = verdict.UncertaintyGap is { } gap
            ? _localizer.Format(
                "Affordability.Uncertain.Range",
                CashFlowDisplay.Date(gap.From),
                CashFlowDisplay.Date(gap.To))
            : _localizer["Affordability.Uncertain.Generic"];
        IsRelative = verdict.IsRelative;

        HasResult = true;
    }
}

/// <summary>
/// An account choice for the affordability picker: the account id (null = all accounts blended), its
/// display name, and its anchor date so the VM can flag a relative answer without a second query.
/// </summary>
public sealed record AccountOption(Guid? Id, string Label, DateOnly? AnchorDate);
