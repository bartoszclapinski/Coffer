using System.Collections.ObjectModel;
using System.Globalization;
using Coffer.Application.Localization;
using Coffer.Core.Backup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Restore;

/// <summary>
/// View-model behind the restore-from-snapshot dialog (Sprint 24). Lists the available local daily
/// snapshots and stages the owner's choice through <see cref="IRestoreService"/>; the actual swap happens
/// at the next startup, so this VM only records the request. A snapshot is the same vault (same master
/// password), so there is no password field. Raises <see cref="CloseRequested"/> once the owner stages a
/// restore or cancels, which the window handles by closing.
/// </summary>
public sealed partial class RestoreViewModel : ObservableObject
{
    private readonly IRestoreService _restoreService;
    private readonly ILocalizer _localizer;
    private readonly ILogger<RestoreViewModel> _logger;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private SnapshotRowViewModel? _selectedSnapshot;

    public RestoreViewModel(
        IRestoreService restoreService,
        ILocalizer localizer,
        ILogger<RestoreViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(restoreService);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);
        _restoreService = restoreService;
        _localizer = localizer;
        _logger = logger;
    }

    /// <summary>Raised when the dialog should close (a restore was staged, or the owner cancelled).</summary>
    public event EventHandler? CloseRequested;

    public ObservableCollection<SnapshotRowViewModel> Snapshots { get; } = [];

    public bool HasSnapshots => Snapshots.Count > 0;

    /// <summary>Whether a restore was staged before the dialog closed (drives the "restart" prompt).</summary>
    public bool Staged { get; private set; }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var snapshots = await _restoreService.ListSnapshotsAsync(CancellationToken.None).ConfigureAwait(true);

            Snapshots.Clear();
            foreach (var snapshot in snapshots)
            {
                Snapshots.Add(new SnapshotRowViewModel(
                    snapshot.Date,
                    snapshot.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    FormatSize(snapshot.SizeBytes)));
            }

            OnPropertyChanged(nameof(HasSnapshots));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list snapshots for restore");
            StatusMessage = _localizer["Restore.Status.LoadFailed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRestore() => SelectedSnapshot is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync()
    {
        if (IsBusy || SelectedSnapshot is not { } selected)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "";
        try
        {
            await _restoreService.StageRestoreAsync(selected.Date, CancellationToken.None).ConfigureAwait(true);
            Staged = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // A snapshot could vanish between listing and staging; keep the dialog open so the owner sees it.
            _logger.LogError(ex, "Failed to stage restore from snapshot {Date}", selected.Date);
            StatusMessage = _localizer["Restore.Status.StageFailed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Format(CultureInfo.InvariantCulture, "{0} {1}", bytes, units[unit])
            : string.Format(CultureInfo.InvariantCulture, "{0:0.0} {1}", size, units[unit]);
    }
}

/// <summary>One selectable snapshot row: its date (for staging) plus display-ready date and size text.</summary>
public sealed record SnapshotRowViewModel(DateOnly Date, string DisplayDate, string DisplaySize);
