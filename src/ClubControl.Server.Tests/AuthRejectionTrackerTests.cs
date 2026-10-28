using ClubControl.Server.Services;

namespace ClubControl.Server.Tests;

public class AuthRejectionTrackerTests
{
    private static readonly Guid Pc = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_first_rejection_is_logged_but_the_ones_every_5_seconds_after_it_are_not()
    {
        var tracker = new AuthRejectionTracker();

        Assert.True(tracker.Record(Pc, T0));

        for (var seconds = 5; seconds < 600; seconds += 5)
            Assert.False(tracker.Record(Pc, T0.AddSeconds(seconds)));
    }

    [Fact]
    public void It_is_logged_again_after_the_log_interval()
    {
        var tracker = new AuthRejectionTracker();
        tracker.Record(Pc, T0);

        Assert.True(tracker.Record(Pc, T0 + AuthRejectionTracker.LogEvery));
        Assert.False(tracker.Record(Pc, T0 + AuthRejectionTracker.LogEvery + TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_PC_counts_as_rejected_only_while_its_heartbeats_keep_being_rejected()
    {
        var tracker = new AuthRejectionTracker();
        Assert.False(tracker.IsRejected(Pc, T0));

        tracker.Record(Pc, T0);

        Assert.True(tracker.IsRejected(Pc, T0.AddSeconds(10)));
        Assert.False(tracker.IsRejected(Pc, T0 + AuthRejectionTracker.ShownFor + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void PCs_are_tracked_separately()
    {
        var tracker = new AuthRejectionTracker();
        var other = Guid.NewGuid();

        tracker.Record(Pc, T0);

        Assert.True(tracker.IsRejected(Pc, T0));
        Assert.False(tracker.IsRejected(other, T0));
        Assert.True(tracker.Record(other, T0.AddSeconds(5)));
    }
}
