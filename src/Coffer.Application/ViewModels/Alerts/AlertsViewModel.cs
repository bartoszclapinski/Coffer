using System.Collections.ObjectModel;
using Coffer.Application.Localization;
using Coffer.Core.Anomalies;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Alerts;

/// <summary>
/// View-model behind the Alerty page. Opening the page runs the (idempotent) anomaly scan and
/// then loads the active alert list; the signature dedup means re-scanning never duplicates or
/// resurrects dismissed alerts. Acknowledging or dismissing a card mutates it through
/// <see cref="IAlertService"/> and drops it from the list immediately.
/// </summary>
public sealed partial class AlertsViewModel : ObservableObject
{
    private readonly IDetectAnomaliesUseCase _detect;
    private readonly IAlertsQuery _query;
    private readonly IAlertService _service;
    private readonly ILocalizer _localizer;
    private readonly ILogger<AlertsViewModel> _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasAlerts;

    public AlertsViewModel(
        IDetectAnomaliesUseCase detect,
        IAlertsQuery query,
        IAlertService service,
        ILocalizer localizer,
        ILogger<AlertsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(detect);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _detect = detect;
        _query = query;
        _service = service;
        _localizer = localizer;
        _logger = logger;
    }

    public ObservableCollection<AlertRowViewModel> Alerts { get; } = [];

    public bool IsEmpty => !IsLoading && !HasAlerts && string.IsNullOrEmpty(ErrorMessage);

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        OnPropertyChanged(nameof(IsEmpty));
        ErrorMessage = "";
        try
        {
            await _detect.RunAsync(ct).ConfigureAwait(true);
            await RefreshAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load alerts");
            ErrorMessage = _localizer["Alerts.Error"];
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var items = await _query.GetActiveAsync(ct).ConfigureAwait(true);

        Alerts.Clear();
        foreach (var item in items)
        {
            Alerts.Add(new AlertRowViewModel(item, AcknowledgeAsync, DismissAsync));
        }

        HasAlerts = Alerts.Count > 0;
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task AcknowledgeAsync(AlertRowViewModel row)
    {
        if (row.IsBusy)
        {
            return;
        }

        row.IsBusy = true;
        try
        {
            await _service.AcknowledgeAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
            Alerts.Remove(row);
            HasAlerts = Alerts.Count > 0;
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acknowledge alert {AlertId}", row.Id);
            row.IsBusy = false;
        }
    }

    private async Task DismissAsync(AlertRowViewModel row)
    {
        if (row.IsBusy)
        {
            return;
        }

        row.IsBusy = true;
        try
        {
            await _service.DismissAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
            Alerts.Remove(row);
            HasAlerts = Alerts.Count > 0;
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dismiss alert {AlertId}", row.Id);
            row.IsBusy = false;
        }
    }
}
