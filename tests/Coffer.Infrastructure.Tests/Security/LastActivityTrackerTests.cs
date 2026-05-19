using Coffer.Infrastructure.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Security;

public class LastActivityTrackerTests
{
    [Fact]
    public void LastActivityUtc_AfterConstruction_IsRecent()
    {
        var before = DateTime.UtcNow;
        var tracker = new LastActivityTracker();
        var after = DateTime.UtcNow;

        tracker.LastActivityUtc.Should()
            .BeOnOrAfter(before)
            .And.BeOnOrBefore(after);
        tracker.LastActivityUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task RegisterActivity_AdvancesLastActivityForward()
    {
        var tracker = new LastActivityTracker();
        var initial = tracker.LastActivityUtc;

        await Task.Delay(15);
        tracker.RegisterActivity();

        tracker.LastActivityUtc.Should().BeAfter(initial);
    }

    [Fact]
    public void RegisterActivity_FromMultipleThreads_DoesNotCorrupt()
    {
        var tracker = new LastActivityTracker();
        var threads = new List<Thread>();

        for (var i = 0; i < 16; i++)
        {
            var thread = new Thread(() =>
            {
                for (var j = 0; j < 200; j++)
                {
                    tracker.RegisterActivity();
                }
            });
            threads.Add(thread);
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        var now = DateTime.UtcNow;
        tracker.LastActivityUtc.Should()
            .BeOnOrBefore(now)
            .And.BeAfter(now.AddSeconds(-5),
                "concurrent writes must not produce a value far in the past via torn reads");
    }
}
