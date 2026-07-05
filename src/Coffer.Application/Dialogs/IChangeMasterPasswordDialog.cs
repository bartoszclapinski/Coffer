namespace Coffer.Application.Dialogs;

/// <summary>
/// Opens the "change master password" dialog. Sits behind an interface so the framework-free
/// <see cref="ViewModels.Settings.SettingsViewModel"/> can trigger it without referencing Avalonia
/// (hard rule #4). Returns <c>true</c> if the password was changed.
/// </summary>
public interface IChangeMasterPasswordDialog
{
    Task<bool> ShowAsync(CancellationToken ct);
}
