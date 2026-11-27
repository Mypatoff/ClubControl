using ClubControl.Server.Data.Models;
using ClubControl.Server.Hubs;
using ClubControl.Server.Services;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>The Delete button (history is kept as "*Old PC 6") and a reinstalled computer taking over its old PC row.</summary>
public class PcRetirementTests : SessionTestBase
{
    private async Task RunAndEndSessionAsync(Guid machineGuid)
    {
        using (var db = NewDb())
            await ServiceFor(db).StartPostpayAsync(machineGuid);
        using (var db = NewDb())
            await ServiceFor(db).EndSessionEarlyAsync(machineGuid);
    }

    private async Task<Pc> ReloadAsync(Guid? machineGuid = null, int? id = null)
    {
        using var db = NewDb();
        return await db.Pcs.AsNoTracking().SingleAsync(p => id != null ? p.Id == id : p.MachineGuid == machineGuid);
    }

    // ---- Delete ----

    [Fact]
    public async Task A_PC_without_any_session_is_removed_completely()
    {
        using (var db = NewDb())
        {
            var pc = await db.Pcs.SingleAsync(p => p.MachineGuid == Pc2);
            Assert.Equal(PcRetirement.Outcome.Deleted, await PcRetirement.RemoveAsync(db, pc));
        }

        using var check = NewDb();
        Assert.False(await check.Pcs.AnyAsync(p => p.MachineGuid == Pc2));
    }

    [Fact]
    public async Task A_PC_with_history_is_renamed_old_loses_its_number_and_is_hidden_but_its_reports_stay()
    {
        await RunAndEndSessionAsync(Pc1);
        var before = await ReloadAsync(Pc1);

        using (var db = NewDb())
        {
            var pc = await db.Pcs.SingleAsync(p => p.Id == before.Id);
            Assert.Equal(PcRetirement.Outcome.Retired, await PcRetirement.RemoveAsync(db, pc));
        }

        var after = await ReloadAsync(id: before.Id);
        Assert.Equal("*Old PC 1", after.Name);
        Assert.True(after.IsRetired);
        Assert.Null(after.Number);
        Assert.False(after.Enabled);
        Assert.Null(after.AuthTokenHash);
        Assert.NotEqual(Pc1, after.MachineGuid);

        using var check = NewDb();
        Assert.Equal(1, await check.Sessions.CountAsync(s => s.PcId == before.Id)); // history is untouched
        var report = await new ReportsService(check).BuildReportAsync(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1), ResourceKind.Computer);
        var line = Assert.Single(report.ByPc);
        Assert.Equal("*Old PC 1", line.PcName);
        Assert.Null(line.PcNumber);
        Assert.Equal("*Old PC 1", Assert.Single(report.Sessions).PcName);
    }

    [Fact]
    public async Task After_a_delete_the_number_is_free_again_and_the_old_row_does_not_count_as_a_waiting_new_PC()
    {
        await RunAndEndSessionAsync(Pc1);
        using (var db = NewDb())
            await PcRetirement.RemoveAsync(db, await db.Pcs.SingleAsync(p => p.MachineGuid == Pc1));

        using var check = NewDb();
        Assert.Equal(0, await NewPcLimit.CountUnassignedAsync(check));
        Assert.DoesNotContain(await check.Pcs.Where(p => !p.IsManual).ToListAsync(), p => p.Number == 1);

        // A new PC 1 can be added and gets its own report line next to the old one.
        var newGuid = Guid.NewGuid();
        var tariffId = (await check.Tariffs.FirstAsync()).Id;
        check.Pcs.Add(new Pc { MachineGuid = newGuid, Number = 1, TariffId = tariffId });
        await check.SaveChangesAsync();
        await RunAndEndSessionAsync(newGuid);

        using var reportDb = NewDb();
        var report = await new ReportsService(reportDb).BuildReportAsync(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1), ResourceKind.Computer);
        Assert.Equal(new[] { "1", "*Old PC 1" }, report.ByPc.Select(r => r.PcNumber?.ToString() ?? r.PcName));
    }

    [Fact]
    public async Task A_running_session_blocks_the_delete_and_changes_nothing()
    {
        using (var db = NewDb())
            await ServiceFor(db).StartPostpayAsync(Pc1);

        using (var db = NewDb())
        {
            var pc = await db.Pcs.SingleAsync(p => p.MachineGuid == Pc1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => PcRetirement.RemoveAsync(db, pc));
        }

        var after = await ReloadAsync(Pc1);
        Assert.Equal(1, after.Number);
        Assert.True(after.Enabled);
    }

    [Fact]
    public async Task Deleting_twice_is_refused_and_a_named_resource_keeps_its_name()
    {
        Guid table = Guid.NewGuid();
        using (var db = NewDb())
        {
            var tariff = new Tariff { Name = "Table tariff", Kind = ResourceKind.Other, PricePerHourUzs = 20_000, MinPaymentUzs = 2_000, MaxPaymentUzs = 90_000 };
            db.Tariffs.Add(tariff);
            await db.SaveChangesAsync();
            db.Pcs.Add(new Pc { MachineGuid = table, Kind = ResourceKind.Other, IsManual = true, Name = "Table 3", Number = 9, TariffId = tariff.Id });
            await db.SaveChangesAsync();
        }
        await RunAndEndSessionAsync(table);

        using var db2 = NewDb();
        var pc = await db2.Pcs.SingleAsync(p => p.MachineGuid == table);
        await PcRetirement.RemoveAsync(db2, pc);
        Assert.Equal("*Old Table 3", pc.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => PcRetirement.RemoveAsync(db2, pc));
    }

    // ---- Windows reinstall ----

    [Fact]
    public async Task A_reinstalled_computer_takes_over_the_old_PC_with_its_number_tariff_and_history()
    {
        await RunAndEndSessionAsync(Pc2);

        // The reinstalled machine connected with a new machine id and became a "New PC".
        var newGuid = Guid.NewGuid();
        using (var hubDb = NewDb())
            await HubFakes.Hub(hubDb, ServiceFor(hubDb), new HeartbeatTracker()).Heartbeat(
                new HeartbeatRequest { MachineGuid = newGuid, Hostname = "NEW-HOST", IpAddress = "10.0.0.50", MacAddress = "AA-BB-CC-DD-EE-FF", ClientVersion = "1.0.0" });

        var oldBefore = await ReloadAsync(Pc2);
        using (var db = NewDb())
        {
            var oldPc = await db.Pcs.SingleAsync(p => p.Id == oldBefore.Id);
            var newPc = await db.Pcs.SingleAsync(p => p.MachineGuid == newGuid);
            Assert.True(PcRetirement.CanTakeOver(oldPc, newPc));
            await PcRetirement.TakeOverAsync(db, oldPc, newPc);
        }

        using var check = NewDb();
        var pcs = await check.Pcs.AsNoTracking().ToListAsync();
        Assert.Equal(3, pcs.Count); // the "New PC" row is gone
        var taken = pcs.Single(p => p.Id == oldBefore.Id);
        Assert.Equal(newGuid, taken.MachineGuid);
        Assert.Equal(2, taken.Number);
        Assert.Equal(oldBefore.TariffId, taken.TariffId);
        Assert.Equal("NEW-HOST", taken.Hostname);
        Assert.Null(taken.AuthTokenHash);
        Assert.Equal(1, await check.Sessions.CountAsync(s => s.PcId == taken.Id)); // reports keep working

        // The next heartbeat of the new machine lands on the old row, not on a fresh "New PC".
        using (var hubDb = NewDb())
            await HubFakes.Hub(hubDb, ServiceFor(hubDb), new HeartbeatTracker()).Heartbeat(
                new HeartbeatRequest { MachineGuid = newGuid, Hostname = "NEW-HOST", IpAddress = "10.0.0.50", MacAddress = "AA-BB-CC-DD-EE-FF", ClientVersion = "1.0.0" });
        using var after = NewDb();
        Assert.Equal(3, await after.Pcs.CountAsync());
    }

    [Fact]
    public async Task Only_an_offline_unretired_networked_computer_can_be_taken_over()
    {
        var fresh = new Pc { MachineGuid = Guid.NewGuid() };
        Assert.True(PcRetirement.CanTakeOver(new Pc { Id = 1, Number = 4 }, fresh));
        Assert.False(PcRetirement.CanTakeOver(new Pc { Id = 1, Number = 4, IsOnline = true }, fresh));
        Assert.False(PcRetirement.CanTakeOver(new Pc { Id = 1, Number = 4, IsManual = true }, fresh));
        Assert.False(PcRetirement.CanTakeOver(new Pc { Id = 1, Kind = ResourceKind.Other, Name = "Table" }, fresh));
        Assert.False(PcRetirement.CanTakeOver(new Pc { Id = 1, Name = "*Old PC 4" }, fresh));
        Assert.False(PcRetirement.CanTakeOver(new Pc { Id = 1, Number = 4 }, new Pc { Id = 1 }));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_machine_that_is_already_set_up_is_not_swallowed_by_a_takeover()
    {
        using var db = NewDb();
        var oldPc = await db.Pcs.SingleAsync(p => p.MachineGuid == Pc1);
        var configured = await db.Pcs.SingleAsync(p => p.MachineGuid == Pc2); // has a number in the database

        await Assert.ThrowsAsync<InvalidOperationException>(() => PcRetirement.TakeOverAsync(db, oldPc, configured));
        Assert.Equal(3, await NewDb().Pcs.CountAsync());
    }
}
