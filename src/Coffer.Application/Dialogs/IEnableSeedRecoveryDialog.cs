namespace Coffer.Application.Dialogs;

/// <summary>
/// Opens the "enable seed recovery" dialog (generate → display → verify a fresh seed → wrap the DEK). Sits
/// behind an interface so the framework-free <see cref="ViewModels.Settings.SettingsViewModel"/> can trigger
/// it without referencing Avalonia (hard rule #4). Returns <c>true</c> if seed recovery was enabled.
/// </summary>
public interface IEnableSeedRecoveryDialog
{
    Task<bool> ShowAsync(CancellationToken ct);
}
