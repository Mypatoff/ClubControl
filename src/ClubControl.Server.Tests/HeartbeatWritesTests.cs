using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Services;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>Heartbeats are noted in memory; the database is written only when something real changed (or about once a minute).</summary>
public class HeartbeatWritesTests : SessionTestBase
{
    private static readonly DateTime T0 = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_newest_heartbeat_wins_between_memory_and_the_saved_one()
    {
        var tracker = new HeartbeatTracker();
        var guid = Guid.NewGuid();

        Assert.Equal(T0, tracker.Latest(guid, T0)); // nothing in memory: the saved one

        tracker.Record(guid, T0.AddSeconds(30));
        Assert.Equal(T0.AddSeconds(30), tracker.Latest(guid, T0));
        Assert.Equal(T0.AddSeconds(90), tracker.Latest(guid, T0.AddSeconds(90))); // saved is newer (after a restart, say)
    }

    [Fact]
    public void The_saved_time_is_refreshed_once_a_minute()
    {
        Assert.False(HeartbeatTracker.ShouldSave(T0, T0.AddSeconds(5)));
        Assert.False(HeartbeatTracker.ShouldSave(T0, T0.AddSeconds(59)));
        Assert.True(HeartbeatTracker.ShouldSave(T0, T0.AddSeconds(60)));
    }

    [Fact]
    public async Task A_normal_heartbeat_does_not_write_the_PC_row_again()
    {
        var heartbeats = new HeartbeatTracker();
        using (var db = NewDb())
            await HubFor(db, heartbeats).Heartbeat(Request(Pc1)); // comes online: this one is saved

        var saved = (await GetPcAsync(Pc1)).LastHeartbeatUtc;
        Assert.NotEqual(default, saved);

        await Task.Delay(30);
        using (var db = NewDb())
            await HubFor(db, heartbeats).Heartbeat(Request(Pc1)); // same PC, same data, a moment later

        Assert.Equal(saved, (await GetPcAsync(Pc1)).LastHeartbeatUtc); // not written: only memory knows the newer time
        Assert.True(heartbeats.Latest(Pc1, saved) > saved);
    }

    [Fact]
    public async Task A_changed_IP_address_is_saved()
    {
        var heartbeats = new HeartbeatTracker();
        using (var db = NewDb())
            await HubFor(db, heartbeats).Heartbeat(Request(Pc1));

        using (var db = NewDb())
            await HubFor(db, heartbeats).Heartbeat(Request(Pc1, ip: "10.0.0.77"));

        Assert.Equal("10.0.0.77", (await GetPcAsync(Pc1)).IpAddress);
    }

    [Fact]
    public async Task A_PC_that_keeps_heartbeating_is_not_marked_offline_even_though_its_saved_time_is_old()
    {
        var heartbeats = new HeartbeatTracker();
        await SetOnlineAsync(Pc1, savedHeartbeat: T0.AddMinutes(-5));
        heartbeats.Record(Pc1, T0.AddSeconds(-3)); // the hub heard it 3 seconds ago

        using var db = NewDb();
        await OfflineMonitorService.CheckAsync(db, heartbeats, new ClientUpdateTracker(), T0);

        Assert.True((await GetPcAsync(Pc1)).IsOnline);
    }

    [Fact]
    public async Task A_PC_that_went_quiet_is_marked_offline_and_keeps_its_real_last_heartbeat()
    {
        var heartbeats = new HeartbeatTracker();
        await SetOnlineAsync(Pc1, savedHeartbeat: T0.AddMinutes(-5));
        heartbeats.Record(Pc1, T0.AddSeconds(-40)); // last heard 40 s ago, the saved time is 5 minutes old

        using (var db = NewDb())
            await OfflineMonitorService.CheckAsync(db, heartbeats, new ClientUpdateTracker(), T0);

        var pc = await GetPcAsync(Pc1);
        Assert.False(pc.IsOnline);
        Assert.Equal(T0.AddSeconds(-40), pc.LastHeartbeatUtc);

        using var check = NewDb();
        Assert.Equal(1, await check.EventLogEntries.CountAsync(e => e.Type == EventType.PcDisconnected && e.PcId == pc.Id));
    }

    [Fact]
    public async Task A_PC_with_no_time_in_memory_falls_back_to_the_saved_heartbeat()
    {
        await SetOnlineAsync(Pc1, savedHeartbeat: T0.AddSeconds(-40));

        using var db = NewDb();
        await OfflineMonitorService.CheckAsync(db, new HeartbeatTracker(), new ClientUpdateTracker(), T0);

        Assert.False((await GetPcAsync(Pc1)).IsOnline);
    }

    [Fact]
    public async Task A_clean_disconnect_saves_the_real_last_heartbeat()
    {
        var heartbeats = new HeartbeatTracker();
        using var db = NewDb();
        var hub = HubFor(db, heartbeats);
        await hub.Heartbeat(Request(Pc1)); // online; the saved heartbeat is "now"

        var saved = (await GetPcAsync(Pc1)).LastHeartbeatUtc;
        heartbeats.Record(Pc1, saved.AddSeconds(40)); // later heartbeats were only noted in memory

        await hub.OnDisconnectedAsync(null); // the same hub instance, so it is the connection the heartbeat registered

        var pc = await GetPcAsync(Pc1);
        Assert.False(pc.IsOnline);
        Assert.Equal(saved.AddSeconds(40), pc.LastHeartbeatUtc);
    }

    private async Task SetOnlineAsync(Guid machineGuid, DateTime savedHeartbeat)
    {
        using var db = NewDb();
        var pc = await db.Pcs.SingleAsync(p => p.MachineGuid == machineGuid);
        pc.IsOnline = true;
        pc.LastHeartbeatUtc = savedHeartbeat;
        await db.SaveChangesAsync();
    }

    private static HeartbeatRequest Request(Guid machineGuid, string ip = "10.0.0.9") =>
        new() { MachineGuid = machineGuid, Hostname = "TEST", IpAddress = ip, MacAddress = "AA-BB-CC-DD-EE-FF", ClientVersion = "1.0.0" };

    private static Hubs.ClubHub HubFor(ClubDbContext db, HeartbeatTracker heartbeats) => HubFakes.Hub(db, ServiceFor(db), heartbeats);
}
