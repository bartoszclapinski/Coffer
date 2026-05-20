using Coffer.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Security;

/// <summary>
/// <see cref="System.Threading.Timer"/>-driven idle monitor. Ticks every 60 seconds
/// and compares elapsed time against the timeout supplied to <see cref="Start"/>;
/// raises <see cref="AutoLockTriggered"/> exactly once per <c>Start</c> call, after
/// calling <see cref="Stop"/> internally so re-entry through the event handler can
/// safely call <see cref="Dispose"/>.
/// </summary>
public sealed class AutoLockMonitor : IAutoLockMonitor
{
    private static readonly TimeSpan _pollPeriod = TimeSpan.FromSeconds(60);

    private readonly ILastActivityTracker _activityTracker;
    private readonly ILogger<AutoLockMonitor> _logger;
    private readonly Lock _sync = new();

    private Timer? _timer;
    private TimeSpan _idleTimeout;
    private bool _disposed;

    public AutoLockMonitor(
        ILastActivityTracker activityTracker,
        ILogger<AutoLockMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(activityTracker);
        ArgumentNullException.ThrowIfNull(logger);

        _activityTracker = activityTracker;
        _logger = logger;
    }

    public event EventHandler? AutoLockTriggered;

    public void Start(TimeSpan idleTimeout)
    {
        if (idleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleTimeout),
                idleTimeout,
                "Idle timeout must be positive.");
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _idleTimeout = idleTimeout;
            if (_timer is null)
            {
                _timer = new Timer(OnTick, state: null, _pollPeriod, _pollPeriod);
                _logger.LogInformation(
                    "Auto-lock monitor started with idle timeout {Timeout}", idleTimeout);
            }
        }
    }

    public void Stop()
    {
        Timer? timer;
        lock (_sync)
        {
            timer = _timer;
            _timer = null;
        }

        if (timer is not null)
        {
            timer.Dispose();
            _logger.LogInformation("Auto-lock monitor stopped");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }
        Stop();
    }

    private void OnTick(object? state)
    {
        TimeSpan idleTimeout;
        lock (_sync)
        {
            if (_timer is null)
            {
                return;
            }
            idleTimeout = _idleTimeout;
        }

        var idle = DateTime.UtcNow - _activityTracker.LastActivityUtc;
        if (idle < idleTimeout)
        {
            return;
        }

        // Stop before raising so re-entry through the handler (e.g. Dispose during
        // logout) does not double-fire the event. The lock above guarantees we win
        // the race against a concurrent Stop / Dispose.
        Stop();

        _logger.LogInformation(
            "Auto-lock idle threshold crossed (idle {Idle}, threshold {Threshold})",
            idle,
            idleTimeout);

        try
        {
            AutoLockTriggered?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutoLockTriggered handler threw");
        }
    }
}
