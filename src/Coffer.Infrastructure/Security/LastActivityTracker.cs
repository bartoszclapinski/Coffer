using Coffer.Core.Security;

namespace Coffer.Infrastructure.Security;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="ILastActivityTracker"/>. The
/// underlying timestamp is stored as <see cref="DateTime.Ticks"/> in a <see cref="long"/>
/// field accessed via <see cref="Interlocked"/> so reads and writes never tear and no
/// lock is needed on the hot UI-event path.
/// </summary>
public sealed class LastActivityTracker : ILastActivityTracker
{
    private long _lastActivityUtcTicks;

    public LastActivityTracker()
    {
        Interlocked.Exchange(ref _lastActivityUtcTicks, DateTime.UtcNow.Ticks);
    }

    public DateTime LastActivityUtc =>
        new(Interlocked.Read(ref _lastActivityUtcTicks), DateTimeKind.Utc);

    public void RegisterActivity() =>
        Interlocked.Exchange(ref _lastActivityUtcTicks, DateTime.UtcNow.Ticks);
}
