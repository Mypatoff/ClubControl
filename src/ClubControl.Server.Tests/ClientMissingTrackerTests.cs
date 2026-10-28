using ClubControl.Server.Services;

namespace ClubControl.Server.Tests;

public class ClientMissingTrackerTests
{
    private static readonly Guid Pc = Guid.NewGuid();
    private static readonly DateTime OfflineSince = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime After(TimeSpan span) => OfflineSince + span;

    [Fact]
    public void A_PC_that_just_went_offline_is_not_checked_yet()
    {
        var tracker = new ClientMissingTracker();

        Assert.False(tracker.ShouldProbe(Pc, OfflineSince, After(ClientMissingTracker.Grace - TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void After_the_grace_time_it_is_checked_and_then_only_every_probe_interval()
    {
        var tracker = new ClientMissingTracker();
        var first = After(ClientMissingTracker.Grace);

        Assert.True(tracker.ShouldProbe(Pc, OfflineSince, first));
        tracker.RecordProbe(Pc, poweredOn: false, first);

        Assert.False(tracker.ShouldProbe(Pc, OfflineSince, first + ClientMissingTracker.ProbeEvery - TimeSpan.FromSeconds(1)));
        Assert.True(tracker.ShouldProbe(Pc, OfflineSince, first + ClientMissingTracker.ProbeEvery));
    }

    [Fact]
    public void A_PC_that_never_answers_is_not_missing_its_client()
    {
        var tracker = new ClientMissingTracker();

        for (var i = 0; i < 5; i++)
            Assert.False(tracker.RecordProbe(Pc, poweredOn: false, After(TimeSpan.FromMinutes(3 + i))));

        Assert.False(tracker.IsMissing(Pc));
    }

    [Fact]
    public void One_answer_is_not_enough_but_the_second_in_a_row_raises_the_alert_once()
    {
        var tracker = new ClientMissingTracker();

        Assert.False(tracker.RecordProbe(Pc, poweredOn: true, After(TimeSpan.FromMinutes(3))));
        Assert.False(tracker.IsMissing(Pc));

        Assert.True(tracker.RecordProbe(Pc, poweredOn: true, After(TimeSpan.FromMinutes(4))));
        Assert.True(tracker.IsMissing(Pc));

        // Already flagged: no second log line.
        Assert.False(tracker.RecordProbe(Pc, poweredOn: true, After(TimeSpan.FromMinutes(5))));
        Assert.True(tracker.IsMissing(Pc));
    }

    [Fact]
    public void A_silent_probe_between_two_answers_starts_the_count_again()
    {
        var tracker = new ClientMissingTracker();

        tracker.RecordProbe(Pc, poweredOn: true, After(TimeSpan.FromMinutes(3)));
        tracker.RecordProbe(Pc, poweredOn: false, After(TimeSpan.FromMinutes(4)));

        Assert.False(tracker.RecordProbe(Pc, poweredOn: true, After(TimeSpan.FromMinutes(5))));
        Assert.False(tracker.IsMissing(Pc));
    }

    [Fact]
    public void A_flagged_PC_that_is_then_switched_off_is_cleared()
    {
        var tracker = new ClientMissingTracker();
        tracker.RecordProbe(Pc, poweredOn: true, After(TimeSpan.FromMinutes(3)));
        tracker.RecordProbe(Pc, poweredOn: true, After(TimeSpan.FromMinutes(4)));

        tracker.RecordProbe(Pc, poweredOn: false, After(TimeSpan.FromMinutes(5)));

        Assert.False(tracker.IsMissing(Pc));
    }

    [Fact]
    public void When_the_client_connects_again_everything_is_forgotten()
    {
        var tracker = new ClientMissingTracker();
        tracker.RecordProbe(Pc, poweredOn: true, After(TimeSpan.FromMinutes(3)));
        tracker.RecordProbe(Pc, poweredOn: true, After(TimeSpan.FromMinutes(4)));

        tracker.Forget(Pc);

        Assert.False(tracker.IsMissing(Pc));
        // A later outage starts from zero: the first answer alone does not raise it.
        Assert.False(tracker.RecordProbe(Pc, poweredOn: true, After(TimeSpan.FromMinutes(30))));
    }

    [Fact]
    public void PCs_are_tracked_separately()
    {
        var tracker = new ClientMissingTracker();
        var other = Guid.NewGuid();
        tracker.RecordProbe(Pc, poweredOn: true, After(TimeSpan.FromMinutes(3)));
        tracker.RecordProbe(Pc, poweredOn: true, After(TimeSpan.FromMinutes(4)));

        Assert.True(tracker.IsMissing(Pc));
        Assert.False(tracker.IsMissing(other));
    }
}
