using Coffer.Core.Security;
using Coffer.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Security;

public class AutoLockMonitorTests
{
    [Fact]
    public async Task Start_WhenIdleExceedsTimeout_RaisesEventOnce()
    {
        // The tracker returns a fixed timestamp from 1 hour ago — every tick sees
        // an idle duration far above the 50 ms threshold, so the event fires on
        // the first tick (~1 minute later under the real period). Wait long enough
        // to observe one tick with a generous safety margin.
        var stale = DateTime.UtcNow.AddHours(-1);
        var tracker = new StaticActivityTracker(stale);
        using var monitor = new AutoLockMonitor(tracker, NullLogger<AutoLockMonitor>.Instance);

        var triggers = 0;
        monitor.AutoLockTriggered += (_, _) => Interlocked.Increment(ref triggers);

        monitor.Start(TimeSpan.FromMilliseconds(50));
        // The Timer is configured with a 60-second period; the first tick happens 60 s
        // after Start. Tests cannot wait 60 seconds. We instead invoke the tick callback
        // via reflection on the private OnTick method, simulating the timer.
        InvokeOnTick(monitor, GetRunId(monitor));
        await Task.Delay(20); // event handler runs synchronously, give a brief settle

        triggers.Should().Be(1);
    }

    [Fact]
    public async Task Start_WhenIdleBelowTimeout_DoesNotRaiseEvent()
    {
        var fresh = DateTime.UtcNow;
        var tracker = new StaticActivityTracker(fresh);
        using var monitor = new AutoLockMonitor(tracker, NullLogger<AutoLockMonitor>.Instance);

        var triggers = 0;
        monitor.AutoLockTriggered += (_, _) => Interlocked.Increment(ref triggers);

        monitor.Start(TimeSpan.FromMinutes(15));
        InvokeOnTick(monitor, GetRunId(monitor));
        await Task.Delay(20);

        triggers.Should().Be(0);
    }

    [Fact]
    public async Task Start_AfterAutoLockTriggered_StopsItself()
    {
        var stale = DateTime.UtcNow.AddHours(-1);
        var tracker = new StaticActivityTracker(stale);
        using var monitor = new AutoLockMonitor(tracker, NullLogger<AutoLockMonitor>.Instance);

        var triggers = 0;
        monitor.AutoLockTriggered += (_, _) => Interlocked.Increment(ref triggers);

        monitor.Start(TimeSpan.FromMilliseconds(50));
        InvokeOnTick(monitor, GetRunId(monitor)); // raises once
        InvokeOnTick(monitor, GetRunId(monitor)); // monitor is internally stopped — must not raise again
        await Task.Delay(20);

        triggers.Should().Be(1);
    }

    [Fact]
    public async Task Start_AfterStopAndRestart_DoesNotFireFromOldTimer()
    {
        // The singleton is reused across logout/login cycles. A callback from the
        // pre-restart timer must recognise its stale generation and bail, rather than
        // stopping the new timer and logging the user out seconds after login.
        var stale = DateTime.UtcNow.AddHours(-1);
        var tracker = new StaticActivityTracker(stale);
        using var monitor = new AutoLockMonitor(tracker, NullLogger<AutoLockMonitor>.Instance);

        var triggers = 0;
        monitor.AutoLockTriggered += (_, _) => Interlocked.Increment(ref triggers);

        monitor.Start(TimeSpan.FromMilliseconds(50));
        var oldRunId = GetRunId(monitor);
        monitor.Stop();
        monitor.Start(TimeSpan.FromMilliseconds(50));

        InvokeOnTick(monitor, oldRunId); // stale timer's callback — must be ignored
        await Task.Delay(20);
        triggers.Should().Be(0, "a callback from the pre-restart timer must not fire");

        InvokeOnTick(monitor, GetRunId(monitor)); // current timer fires normally
        await Task.Delay(20);
        triggers.Should().Be(1);
    }

    [Fact]
    public void Dispose_DuringActiveTimer_DoesNotThrow()
    {
        var tracker = new StaticActivityTracker(DateTime.UtcNow);
        var monitor = new AutoLockMonitor(tracker, NullLogger<AutoLockMonitor>.Instance);

        monitor.Start(TimeSpan.FromMinutes(15));
        var act = () => monitor.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Start_WithNonPositiveTimeout_Throws()
    {
        var tracker = new StaticActivityTracker(DateTime.UtcNow);
        using var monitor = new AutoLockMonitor(tracker, NullLogger<AutoLockMonitor>.Instance);

        var act = () => monitor.Start(TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static void InvokeOnTick(AutoLockMonitor monitor, int runId)
    {
        // The Timer's 60-second cadence is fine for production but unobservable in
        // tests; reach into the private callback directly to assert the policy. The
        // runId is the generation token the real Timer passes as its state.
        var method = typeof(AutoLockMonitor)
            .GetMethod("OnTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Should().NotBeNull("the Timer callback must exist to be invokable");
        method!.Invoke(monitor, new object?[] { runId });
    }

    private static int GetRunId(AutoLockMonitor monitor)
    {
        var field = typeof(AutoLockMonitor)
            .GetField("_runId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.Should().NotBeNull("the generation token backs the stale-tick guard");
        return (int)field!.GetValue(monitor)!;
    }

    private sealed class StaticActivityTracker : ILastActivityTracker
    {
        public StaticActivityTracker(DateTime lastActivityUtc)
        {
            LastActivityUtc = lastActivityUtc;
        }

        public DateTime LastActivityUtc { get; }

        public void RegisterActivity()
        {
            // No-op for tests; we want a fixed timestamp.
        }
    }
}
