using System.Security.Claims;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Hubs;
using ClubControl.Server.Services;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>A flood of heartbeats from made-up machine ids must not create unlimited "New PC" rows.</summary>
public class NewPcLimitTests : SessionTestBase
{
    public NewPcLimitTests() => NewPcLimit.ResetWarning();

    [Fact]
    public void The_limit_is_reached_at_ten_waiting_PCs()
    {
        Assert.False(NewPcLimit.IsFull(NewPcLimit.MaxUnassigned - 1));
        Assert.True(NewPcLimit.IsFull(NewPcLimit.MaxUnassigned));
    }

    [Fact]
    public async Task A_new_PC_is_added_while_there_is_room()
    {
        using var db = NewDb();
        var hub = HubFor(db);

        await hub.Heartbeat(Request(Guid.NewGuid()));

        using var check = NewDb();
        Assert.Equal(1, await check.Pcs.CountAsync(p => p.Number == null));
    }

    [Fact]
    public async Task A_flood_of_new_machine_ids_stops_at_the_limit_and_logs_one_warning()
    {
        using var db = NewDb();
        var hub = HubFor(db);

        for (var i = 0; i < NewPcLimit.MaxUnassigned + 25; i++)
            await hub.Heartbeat(Request(Guid.NewGuid()));

        using var check = NewDb();
        Assert.Equal(NewPcLimit.MaxUnassigned, await check.Pcs.CountAsync(p => p.Number == null));
        Assert.Equal(1, await check.EventLogEntries.CountAsync(e => e.Type == EventType.Warning));
    }

    [Fact]
    public async Task A_PC_that_is_already_known_still_connects_when_the_limit_is_full()
    {
        using (var db = NewDb())
        {
            for (var i = 0; i < NewPcLimit.MaxUnassigned; i++)
                db.Pcs.Add(new Pc { MachineGuid = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }

        using var hubDb = NewDb();
        await HubFor(hubDb).Heartbeat(Request(Pc1)); // PC #1 has a number and was seeded before

        Assert.True((await GetPcAsync(Pc1)).IsOnline);
    }

    [Fact]
    public async Task Manual_resources_do_not_count_towards_the_limit()
    {
        using (var db = NewDb())
        {
            for (var i = 0; i < NewPcLimit.MaxUnassigned; i++)
                db.Pcs.Add(new Pc { MachineGuid = Guid.NewGuid(), IsManual = true, Kind = ResourceKind.Other, Name = $"Table {i}" });
            await db.SaveChangesAsync();
        }

        using var hubDb = NewDb();
        await HubFor(hubDb).Heartbeat(Request(Guid.NewGuid()));

        using var check = NewDb();
        Assert.Equal(1, await check.Pcs.CountAsync(p => !p.IsManual && p.Number == null));
    }

    private static HeartbeatRequest Request(Guid machineGuid) =>
        new() { MachineGuid = machineGuid, Hostname = "TEST", IpAddress = "10.0.0.9", MacAddress = "AA-BB-CC-DD-EE-FF", ClientVersion = "1.0.0" };

    private static ClubHub HubFor(Data.ClubDbContext db) => HubFakes.Hub(db, ServiceFor(db), new HeartbeatTracker());
}
