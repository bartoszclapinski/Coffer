namespace Coffer.Application.ViewModels.Login;

/// <summary>
/// Marker event args raised by <see cref="LoginViewModel"/> after a successful
/// <see cref="ILoginService.LoginWithPasswordAsync"/>. Empty today; future sprints
/// may attach context (e.g. which auth mode was used) without breaking subscribers.
/// </summary>
public sealed class LoginCompletedEventArgs : EventArgs
{
    public static LoginCompletedEventArgs Instance { get; } = new();
}
