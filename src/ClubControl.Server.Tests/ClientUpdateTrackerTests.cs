using ClubControl.Server.Services;

namespace ClubControl.Server.Tests;

public class ClientUpdateTrackerTests
{
    private static readonly Guid Pc = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static ClientUpdateTracker Started(string versionBefore = "1.0.0+old")
    {
        var tracker = new ClientUpdateTracker();
        tracker.Started(Pc, versionBefore, "conn-1", T0);
        return tracker;
    }

    [Fact]
    public void A_PC_that_was_sent_the_command_shows_as_updating()
    {
        var tracker = Started();

        var info = tracker.Get(Pc, T0.AddSeconds(5));

        Assert.Equal(ClientUpdateStatus.Updating, info!.Status);
    }

    [Fact]
    public void A_PC_waiting_for_its_turn_shows_as_waiting_and_never_times_out()
    {
        var tracker = new ClientUpdateTracker();
        tracker.Queued(Pc, "1.0.0+old", T0);

        Assert.Equal(ClientUpdateStatus.Waiting, tracker.Get(Pc, T0.AddMinutes(30))!.Status);
        Assert.Empty(tracker.CheckTimeouts(T0.AddMinutes(30))); // the 5 minutes count from when the command is sent
        Assert.Null(tracker.OnHeartbeat(Pc, "1.0.0+old", "conn-2", T0.AddMinutes(1)));
    }

    [Fact]
    public void A_waiting_PC_starts_its_five_minutes_when_the_command_is_sent_and_can_be_cancelled()
    {
        var tracker = new ClientUpdateTracker();
        tracker.Queued(Pc, "1.0.0+old", T0);
        tracker.Started(Pc, "1.0.0+old", "conn-1", T0.AddMinutes(3));

        Assert.Empty(tracker.CheckTimeouts(T0.AddMinutes(7)));   // only 4 minutes since it was sent
        Assert.Single(tracker.CheckTimeouts(T0.AddMinutes(8)));  // 5 minutes since it was sent

        var other = Guid.NewGuid();
        tracker.Queued(other, "1.0.0+old", T0);
        tracker.Cancel(other);
        Assert.Null(tracker.Get(other, T0));
    }

    [Fact]
    public void A_PC_that_was_never_updated_shows_nothing()
    {
        Assert.Null(new ClientUpdateTracker().Get(Pc, T0));
        Assert.Null(new ClientUpdateTracker().OnHeartbeat(Pc, "1.0.0", "conn-1", T0));
    }

    [Fact]
    public void Heartbeats_of_the_old_build_on_the_same_connection_change_nothing()
    {
        var tracker = Started();

        Assert.Null(tracker.OnHeartbeat(Pc, "1.0.0+old", "conn-1", T0.AddSeconds(5)));
        Assert.Equal(ClientUpdateStatus.Updating, tracker.Get(Pc, T0.AddSeconds(6))!.Status);
    }

    [Fact]
    public void A_new_version_means_the_update_worked_and_is_reported_once()
    {
        var tracker = Started();

        var info = tracker.OnHeartbeat(Pc, "1.0.0+new", "conn-2", T0.AddSeconds(40));

        Assert.Equal(ClientUpdateStatus.Updated, info!.Status);
        Assert.Equal("1.0.0+old", info.VersionBefore);
        Assert.Equal("1.0.0+new", info.VersionNow);
        Assert.Null(tracker.OnHeartbeat(Pc, "1.0.0+new", "conn-2", T0.AddSeconds(45))); // not logged again
    }

    [Fact]
    public void A_restart_with_the_same_version_is_reported_as_unchanged()
    {
        var tracker = Started();

        var info = tracker.OnHeartbeat(Pc, "1.0.0+old", "conn-2", T0.AddSeconds(30));

        Assert.Equal(ClientUpdateStatus.Unchanged, info!.Status);
        Assert.True(ClientUpdateTracker.IsProblem(info));
    }

    [Fact]
    public void Silence_for_five_minutes_is_reported_as_no_response_once()
    {
        var tracker = Started();

        Assert.Empty(tracker.CheckTimeouts(T0.AddMinutes(4)));

        var timedOut = tracker.CheckTimeouts(T0.AddMinutes(5));
        Assert.Equal(Pc, Assert.Single(timedOut).MachineGuid);
        Assert.Equal(ClientUpdateStatus.NoResponse, timedOut[0].Info.Status);
        Assert.Empty(tracker.CheckTimeouts(T0.AddMinutes(6))); // reported once
    }

    [Fact]
    public void A_late_new_version_after_no_response_still_counts_as_updated()
    {
        var tracker = Started();
        tracker.CheckTimeouts(T0.AddMinutes(6));

        var info = tracker.OnHeartbeat(Pc, "1.0.0+new", "conn-3", T0.AddMinutes(9));

        Assert.Equal(ClientUpdateStatus.Updated, info!.Status);
    }

    [Fact]
    public void A_failure_stays_on_the_card_until_fixed_but_a_success_fades()
    {
        var failed = Started();
        failed.CheckTimeouts(T0.AddMinutes(6));
        Assert.Equal(ClientUpdateStatus.NoResponse, failed.Get(Pc, T0.AddHours(3))!.Status);

        var worked = Started();
        worked.OnHeartbeat(Pc, "1.0.0+new", "conn-2", T0.AddSeconds(30));
        Assert.NotNull(worked.Get(Pc, T0.AddSeconds(60)));
        Assert.Null(worked.Get(Pc, T0.AddMinutes(10)));
    }

    [Fact]
    public void Updating_again_starts_a_fresh_attempt()
    {
        var tracker = Started();
        tracker.CheckTimeouts(T0.AddMinutes(6));

        tracker.Started(Pc, "1.0.0+old", "conn-9", T0.AddMinutes(20));

        Assert.Equal(ClientUpdateStatus.Updating, tracker.Get(Pc, T0.AddMinutes(20))!.Status);
    }

    [Fact]
    public void The_log_lines_name_the_PC_and_the_versions()
    {
        var tracker = Started();
        var updated = tracker.OnHeartbeat(Pc, "1.0.0+new", "conn-2", T0.AddSeconds(30))!;

        Assert.Equal("PC #5 updated: v1.0.0+old -> v1.0.0+new", ClientUpdateTracker.LogMessage("PC #5", updated));

        var failed = Started();
        var timedOut = failed.CheckTimeouts(T0.AddMinutes(5)).Single().Info;
        Assert.Contains("PC #5 did not update", ClientUpdateTracker.LogMessage("PC #5", timedOut));
        Assert.Contains("client-log.txt", ClientUpdateTracker.LogMessage("PC #5", timedOut));
    }
}
