namespace Coffer.Application.Dialogs;

/// <summary>
/// Opens the restore-from-snapshot dialog. Sits behind an interface (like the file picker) so the
/// framework-free <see cref="ViewModels.Settings.SettingsViewModel"/> can trigger a window without
/// referencing Avalonia (hard rule #4). Returns <c>true</c> if the owner staged a restore (so the caller
/// can prompt for the restart that applies it), <c>false</c> if they cancelled.
/// </summary>
public interface IRestoreDialogService
{
    Task<bool> ShowRestoreDialogAsync(CancellationToken ct);
}
