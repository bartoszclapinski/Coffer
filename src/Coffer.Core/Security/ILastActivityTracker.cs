namespace Coffer.Core.Security;

/// <summary>
/// Thread-safe in-memory record of the last user activity (UI input). The
/// <see cref="IAutoLockMonitor"/> polls <see cref="LastActivityUtc"/> on its tick to
/// decide whether the idle threshold has been exceeded.
/// </summary>
public interface ILastActivityTracker
{
    /// <summary>
    /// The instant of the most recent <see cref="RegisterActivity"/> call. Defaults
    /// to the construction time so the first idle check has a sensible value.
    /// </summary>
    DateTime LastActivityUtc { get; }

    /// <summary>
    /// Sets <see cref="LastActivityUtc"/> to <see cref="DateTime.UtcNow"/>. Safe to
    /// call from any thread; called from the UI thread today via top-level pointer
    /// and key events.
    /// </summary>
    void RegisterActivity();
}
