namespace Coffer.Core.Security;

/// <summary>
/// Periodically inspects <see cref="ILastActivityTracker.LastActivityUtc"/> and raises
/// <see cref="AutoLockTriggered"/> once when the configured idle threshold has been
/// crossed. Implementations call <see cref="Stop"/> internally before raising so the
/// event fires at most once per <see cref="Start"/> invocation. The event is raised
/// on a background thread; subscribers that touch UI state must marshal themselves.
/// </summary>
public interface IAutoLockMonitor : IDisposable
{
    /// <summary>Raised exactly once when idle time first equals or exceeds the configured timeout.</summary>
    event EventHandler? AutoLockTriggered;

    /// <summary>
    /// Begins polling. Calling <see cref="Start"/> while already started is a no-op
    /// (the existing schedule continues with the new timeout if it differs).
    /// </summary>
    void Start(TimeSpan idleTimeout);

    /// <summary>Cancels any pending poll. Safe to call when not started.</summary>
    void Stop();
}
