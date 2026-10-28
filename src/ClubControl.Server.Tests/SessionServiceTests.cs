using ClubControl.Server.Data.Models;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>SessionService against a real SQLite database (SPEC section 5).</summary>
public class SessionServiceTests : SessionTestBase
{
    // Just under 30 whole minutes: 30 started minutes at 10 000 UZS/hour = 5 000 UZS.
    private static readonly TimeSpan AlmostThirtyMinutes = TimeSpan.FromSeconds(29 * 60 + 30);

    /// <summary>
    /// Backdating edits the database directly, so the service that acts on it afterwards must use a
    /// fresh DbContext - exactly what the real server does (one scope per request/tick).
    /// </summary>
    private async Task StartPostpayThenBackdateAsync(Guid pc, TimeSpan shift)
    {
        using (var db = NewDb())
            await ServiceFor(db).StartPostpayAsync(pc);
        await BackdateSessionAsync(shift);
    }

    // ---- prepay ----

    [Fact]
    public async Task PrepayByMoney_buys_whole_minutes_and_locks_in_the_payment()
    {
        using var db = NewDb();
        await ServiceFor(db).StartPrepayByMoneyAsync(Pc1, 5_000);

        var session = await GetOnlySessionAsync();
        Assert.Equal(SessionType.Prepay, session.Type);
        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.Equal(5_000, session.AmountUzs);
        Assert.Equal(30, (session.PlannedEndUtc!.Value - session.StartUtc).TotalMinutes, precision: 3);
        Assert.Equal(PcState.PrepaySession, (await GetPcAsync(Pc1)).State);
        Assert.Single(session.Segments);
        Assert.Equal(StandardPrice, session.Segments[0].PricePerHourUzs);
    }

    [Fact]
    public async Task PrepayByTime_charges_the_price_of_those_minutes()
    {
        using var db = NewDb();
        await ServiceFor(db).StartPrepayByTimeAsync(Pc1, 45);

        // 45 min x 10 000 / 60 = 7 500
        Assert.Equal(7_500, (await GetOnlySessionAsync()).AmountUzs);
    }

    [Fact]
    public async Task Starting_a_session_writes_an_event_log_entry()
    {
        using var db = NewDb();
        await ServiceFor(db).StartPrepayByMoneyAsync(Pc1, 5_000);

        using var check = NewDb();
        Assert.Contains(await check.EventLogEntries.ToListAsync(), e => e.Type == EventType.SessionStarted);
    }

    [Theory]
    [InlineData(1_000)]   // below the tariff minimum
    [InlineData(60_000)]  // above the tariff maximum
    public async Task PrepayByMoney_outside_the_tariff_range_is_rejected_and_nothing_is_saved(int money)
    {
        using var db = NewDb();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ServiceFor(db).StartPrepayByMoneyAsync(Pc1, money));

        using var check = NewDb();
        Assert.Empty(await check.Sessions.ToListAsync());
        Assert.Equal(PcState.Free, (await GetPcAsync(Pc1)).State);
    }

    [Fact]
    public async Task A_pc_with_an_active_session_cannot_start_another()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPrepayByMoneyAsync(Pc1, 5_000);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartPostpayAsync(Pc1));
    }

    [Fact]
    public async Task A_disabled_pc_cannot_start_a_session()
    {
        using (var setup = NewDb())
        {
            var pc = await setup.Pcs.SingleAsync(p => p.MachineGuid == Pc1);
            pc.Enabled = false;
            await setup.SaveChangesAsync();
        }

        using var db = NewDb();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ServiceFor(db).StartPostpayAsync(Pc1));
    }

    [Fact]
    public async Task A_tariff_with_no_price_cannot_bill_anything()
    {
        using (var setup = NewDb())
        {
            var tariff = await setup.Tariffs.SingleAsync(t => t.Name == "Standard");
            tariff.PricePerHourUzs = 0;
            await setup.SaveChangesAsync();
        }

        using var db = NewDb();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ServiceFor(db).StartPrepayByMoneyAsync(Pc1, 5_000));
    }

    [Fact]
    public async Task Stopping_a_prepay_session_halfway_records_only_the_money_used()
    {
        using (var before = NewDb())
            await ServiceFor(before).StartPrepayByMoneyAsync(Pc1, 10_000); // 60 min at 10 000/h
        await BackdateSessionAsync(TimeSpan.FromMinutes(30));              // 30 min used, 30 min left

        using var db = NewDb();
        await ServiceFor(db).EndSessionEarlyAsync(Pc1);

        var session = await GetOnlySessionAsync();
        Assert.Equal(SessionStatus.Ended, session.Status);
        Assert.InRange(session.AmountUzs, 5_000, 5_200); // 5 000 used; the unused part is rounded down to 100 for the refund
        Assert.NotNull(session.ActualEndUtc);
        Assert.All(session.Segments, s => Assert.NotNull(s.EndUtc));
        Assert.Equal(PcState.Locked, (await GetPcAsync(Pc1)).State);
    }

    [Fact]
    public async Task The_income_recorded_plus_the_refund_shown_is_what_was_paid()
    {
        using (var before = NewDb())
            await ServiceFor(before).StartPrepayByMoneyAsync(Pc1, 10_000);
        await BackdateSessionAsync(TimeSpan.FromMinutes(20));

        int refundShown;
        using (var db = NewDb())
            refundShown = (await ServiceFor(db).GetActiveSessionSnapshotAsync(Pc1))!.RemainingMoneyUzs;

        using (var db = NewDb())
            await ServiceFor(db).EndSessionEarlyAsync(Pc1);

        // The snapshot and the stop are a moment apart, so allow one 100 UZS rounding step.
        Assert.InRange((await GetOnlySessionAsync()).AmountUzs + refundShown, 10_000, 10_100);
    }

    [Fact]
    public async Task Stopping_a_prepay_session_whose_time_has_run_out_keeps_it_fully_paid()
    {
        using (var before = NewDb())
            await ServiceFor(before).StartPrepayByMoneyAsync(Pc1, 10_000);
        await BackdateSessionAsync(TimeSpan.FromMinutes(61)); // planned end already passed, nothing left to refund

        using var db = NewDb();
        await ServiceFor(db).EndSessionEarlyAsync(Pc1);

        Assert.Equal(10_000, (await GetOnlySessionAsync()).AmountUzs);
    }

    [Fact]
    public async Task A_prepay_session_reports_the_unused_money_for_the_stop_confirmation()
    {
        using (var before = NewDb())
            await ServiceFor(before).StartPrepayByMoneyAsync(Pc1, 10_000); // 60 min at 10 000/h
        await BackdateSessionAsync(TimeSpan.FromMinutes(30));              // 30 min left = about 5 000 UZS

        using var db = NewDb();
        var snapshot = await ServiceFor(db).GetActiveSessionSnapshotAsync(Pc1);

        Assert.NotNull(snapshot);
        Assert.InRange(snapshot!.RemainingMoneyUzs, 4_800, 5_000);
        Assert.Equal(10_000, snapshot.AmountOrCostUzs); // what was paid is unchanged
    }

    [Fact]
    public async Task A_postpay_session_has_no_unused_prepay_money()
    {
        using (var before = NewDb())
            await ServiceFor(before).StartPostpayAsync(Pc1);

        using var db = NewDb();
        var snapshot = await ServiceFor(db).GetActiveSessionSnapshotAsync(Pc1);

        Assert.Equal(0, snapshot!.RemainingMoneyUzs);
    }

    // ---- postpay and free ----

    [Fact]
    public async Task Postpay_is_charged_per_started_minute_when_the_admin_ends_it()
    {
        await StartPostpayThenBackdateAsync(Pc1, AlmostThirtyMinutes);

        using var db = NewDb();
        await ServiceFor(db).EndSessionEarlyAsync(Pc1);

        var session = await GetOnlySessionAsync();
        Assert.Equal(5_000, session.AmountUzs);
        Assert.Equal(SessionStatus.Ended, session.Status);
        Assert.Equal(PcState.Locked, (await GetPcAsync(Pc1)).State);
    }

    // ---- staff unlock (Ctrl+Shift+F12) puts the PC in Repair ----

    [Fact]
    public async Task A_PC_opened_with_the_unlock_password_is_marked_as_repair()
    {
        using var db = NewDb();

        var marked = await ServiceFor(db).MarkManualUnlockAsync(Pc1);

        Assert.True(marked);
        Assert.Equal(PcState.ManualUnlocked, (await GetPcAsync(Pc1)).State);
    }

    [Fact]
    public async Task Unlocking_a_PC_that_is_waiting_for_payment_does_not_hide_its_session()
    {
        await StartPostpayThenBackdateAsync(Pc1, TimeSpan.FromHours(6));
        using (var db = NewDb())
            await ServiceFor(db).TickPostpayCostsAsync(); // reaches the maximum: LimitReached

        using var db2 = NewDb();
        var marked = await ServiceFor(db2).MarkManualUnlockAsync(Pc1);

        Assert.False(marked);
        Assert.Equal(PcState.LimitReached, (await GetPcAsync(Pc1)).State);
        Assert.Equal(SessionStatus.Active, (await GetOnlySessionAsync()).Status);
    }

    [Fact]
    public async Task A_PC_in_repair_is_locked_again_when_it_reconnects()
    {
        using (var db = NewDb())
            await ServiceFor(db).MarkManualUnlockAsync(Pc1);

        using var db2 = NewDb();
        var command = await ServiceFor(db2).BuildCurrentCommandAsync(await db2.Pcs.FirstAsync(p => p.MachineGuid == Pc1));

        Assert.Equal(CommandType.Lock, command!.Type);
        Assert.Equal(PcState.Locked, (await GetPcAsync(Pc1)).State);
    }

    [Fact]
    public async Task A_session_can_be_started_on_a_PC_in_repair()
    {
        using (var db = NewDb())
            await ServiceFor(db).MarkManualUnlockAsync(Pc1);

        using var db2 = NewDb();
        await ServiceFor(db2).StartPrepayByMoneyAsync(Pc1, 5_000);

        Assert.Equal(PcState.PrepaySession, (await GetPcAsync(Pc1)).State);
    }

    [Fact]
    public async Task Postpay_is_never_charged_below_the_tariff_minimum()
    {
        await StartPostpayThenBackdateAsync(Pc1, TimeSpan.FromSeconds(270)); // 5 started minutes = 900 UZS

        using var db = NewDb();
        await ServiceFor(db).EndSessionEarlyAsync(Pc1);

        Assert.Equal(StandardMin, (await GetOnlySessionAsync()).AmountUzs);
    }

    [Fact]
    public async Task Postpay_reaching_the_maximum_locks_the_pc_and_waits_for_the_admin()
    {
        await StartPostpayThenBackdateAsync(Pc1, TimeSpan.FromHours(6)); // 60 000 UZS, above the 50 000 maximum

        using var db = NewDb();
        await ServiceFor(db).TickPostpayCostsAsync();

        Assert.Equal(PcState.LimitReached, (await GetPcAsync(Pc1)).State);
        Assert.Equal(SessionStatus.Active, (await GetOnlySessionAsync()).Status);

        // The admin then takes the money and confirms: the customer pays the maximum, not more.
        using var db2 = NewDb();
        await ServiceFor(db2).EndSessionEarlyAsync(Pc1);

        var session = await GetOnlySessionAsync();
        Assert.Equal(StandardMax, session.AmountUzs);
        Assert.Equal(SessionStatus.Ended, session.Status);
        Assert.Equal(PcState.Locked, (await GetPcAsync(Pc1)).State);
    }

    [Fact]
    public async Task Postpay_below_the_maximum_keeps_running_after_a_tick()
    {
        await StartPostpayThenBackdateAsync(Pc1, AlmostThirtyMinutes);

        using var db = NewDb();
        await ServiceFor(db).TickPostpayCostsAsync();

        Assert.Equal(PcState.PostpaySession, (await GetPcAsync(Pc1)).State);
    }

    [Fact]
    public async Task The_note_typed_at_start_is_saved_with_the_session_and_is_optional()
    {
        using (var db = NewDb())
        {
            var service = ServiceFor(db);
            await service.StartPrepayByMoneyAsync(Pc1, 5_000, "  birthday group  ");
            await service.StartPostpayAsync(Pc2, "   ");
        }

        using var check = NewDb();
        var sessions = await check.Sessions.AsNoTracking().Include(s => s.Pc).ToListAsync();
        Assert.Equal("birthday group", sessions.Single(s => s.Pc!.MachineGuid == Pc1).Note);
        Assert.Null(sessions.Single(s => s.Pc!.MachineGuid == Pc2).Note);
        Assert.Contains(await check.EventLogEntries.ToListAsync(), e => e.Message.Contains("birthday group"));
    }

    [Fact]
    public async Task Ending_a_pc_with_no_session_is_rejected()
    {
        using var db = NewDb();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ServiceFor(db).EndSessionEarlyAsync(Pc1));
    }

    // ---- move ----

    [Fact]
    public async Task Moving_a_prepay_session_keeps_the_remaining_time()
    {
        using (var before = NewDb())
            await ServiceFor(before).StartPrepayByMoneyAsync(Pc1, 10_000); // 60 min at 10 000/h
        await BackdateSessionAsync(TimeSpan.FromMinutes(30));              // 30 min left

        using var db = NewDb();
        await ServiceFor(db).MoveSessionAsync(Pc1, Pc2);

        // Same tariff, so the time left is untouched by the move.
        var session = await GetOnlySessionAsync();
        var minutesLeft = (session.PlannedEndUtc!.Value - DateTime.UtcNow).TotalMinutes;
        Assert.InRange(minutesLeft, 29.0, 30.1);
        Assert.Equal(10_000, session.AmountUzs);

        Assert.Equal(2, session.Segments.Count);
        Assert.Equal(StandardPrice, session.Segments.Single(s => s.EndUtc != null).PricePerHourUzs);
        Assert.Equal(StandardPrice, session.Segments.Single(s => s.EndUtc == null).PricePerHourUzs);

        Assert.Equal(PcState.Locked, (await GetPcAsync(Pc1)).State);
        Assert.Equal(PcState.PrepaySession, (await GetPcAsync(Pc2)).State);
        Assert.Equal((await GetPcAsync(Pc2)).Id, session.PcId);
    }

    [Fact]
    public async Task Moving_a_postpay_session_keeps_the_cost_so_far()
    {
        await StartPostpayThenBackdateAsync(Pc1, AlmostThirtyMinutes); // 5 000 UZS

        using (var db = NewDb())
            await ServiceFor(db).MoveSessionAsync(Pc1, Pc2);
        using var db2 = NewDb();
        await ServiceFor(db2).EndSessionEarlyAsync(Pc2);

        Assert.Equal(5_000, (await GetOnlySessionAsync()).AmountUzs);
    }

    [Fact]
    public async Task A_session_cannot_be_moved_to_a_different_tariff()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPrepayByMoneyAsync(Pc1, 5_000);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveSessionAsync(Pc1, VipPc));

        Assert.Equal(PcState.PrepaySession, (await GetPcAsync(Pc1)).State);
        Assert.Equal(PcState.Free, (await GetPcAsync(VipPc)).State);
    }

    [Fact]
    public async Task A_session_cannot_be_moved_to_a_different_kind_of_resource()
    {
        using (var setup = NewDb())
        {
            var tariffId = (await setup.Pcs.SingleAsync(p => p.MachineGuid == Pc1)).TariffId;
            setup.Pcs.Add(new Pc { MachineGuid = OtherGuid, Number = 4, Kind = ResourceKind.Other, TariffId = tariffId });
            await setup.SaveChangesAsync();
        }

        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPrepayByMoneyAsync(Pc1, 5_000);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveSessionAsync(Pc1, OtherGuid));
    }

    [Fact]
    public async Task Two_running_sessions_can_be_swapped()
    {
        using (var db = NewDb())
        {
            var service = ServiceFor(db);
            await service.StartPrepayByMoneyAsync(Pc1, 10_000);
            await service.StartPostpayAsync(Pc2);
        }

        using (var db = NewDb())
            await ServiceFor(db).SwapSessionsAsync(Pc1, Pc2);

        using var check = NewDb();
        var pc1 = await check.Pcs.SingleAsync(p => p.MachineGuid == Pc1);
        var pc2 = await check.Pcs.SingleAsync(p => p.MachineGuid == Pc2);
        var sessions = await check.Sessions.Include(s => s.Segments).ToListAsync();
        var prepay = sessions.Single(s => s.Type == SessionType.Prepay);
        var postpay = sessions.Single(s => s.Type == SessionType.Postpay);

        // The prepay session now sits on PC 2 and the postpay one on PC 1, each with a new open segment there.
        Assert.Equal(pc2.Id, prepay.PcId);
        Assert.Equal(pc1.Id, postpay.PcId);
        Assert.Equal(PcState.PrepaySession, pc2.State);
        Assert.Equal(PcState.PostpaySession, pc1.State);
        Assert.Equal(pc2.Id, prepay.Segments.Single(s => s.EndUtc == null).PcId);
        Assert.Equal(pc1.Id, postpay.Segments.Single(s => s.EndUtc == null).PcId);
        Assert.Equal(SessionStatus.Active, prepay.Status);
        Assert.Equal(SessionStatus.Active, postpay.Status);
    }

    [Fact]
    public async Task Swapping_needs_both_pcs_to_be_running_and_on_the_same_tariff()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPrepayByMoneyAsync(Pc1, 5_000);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SwapSessionsAsync(Pc1, Pc2));   // Pc2 is idle

        await service.StartPostpayAsync(VipPc);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SwapSessionsAsync(Pc1, VipPc)); // different tariff
    }

    [Fact]
    public async Task A_session_cannot_be_moved_to_a_busy_pc()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPrepayByMoneyAsync(Pc1, 5_000);
        await service.StartPostpayAsync(Pc2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveSessionAsync(Pc1, Pc2));

        using var check = NewDb();
        Assert.Equal(PcState.PrepaySession, (await GetPcAsync(Pc1)).State);
        Assert.Equal(2, await check.Sessions.CountAsync());
    }

    [Fact]
    public async Task A_session_cannot_be_moved_to_the_same_pc()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPrepayByMoneyAsync(Pc1, 5_000);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveSessionAsync(Pc1, Pc1));
    }

    // ---- server restart / power cut (SPEC 5) ----

    [Fact]
    public async Task Restart_closes_a_prepay_session_that_ran_out_while_the_server_was_down_at_its_planned_end()
    {
        using (var before = NewDb())
            await ServiceFor(before).StartPrepayByMoneyAsync(Pc1, 10_000); // 60 min
        await BackdateSessionAsync(TimeSpan.FromMinutes(90));              // ended 30 min ago

        using (var restarted = NewDb())
            await ServiceFor(restarted).RestoreActiveSessionsAsync();

        var session = await GetOnlySessionAsync();
        Assert.Equal(SessionStatus.Ended, session.Status);
        Assert.Equal(session.PlannedEndUtc, session.ActualEndUtc); // closed at the planned end, not "now"
        Assert.Equal(10_000, session.AmountUzs);
        Assert.Equal(PcState.Locked, (await GetPcAsync(Pc1)).State);
    }

    [Fact]
    public async Task Restart_keeps_a_prepay_session_that_still_has_time_left()
    {
        using (var before = NewDb())
            await ServiceFor(before).StartPrepayByMoneyAsync(Pc1, 10_000); // 60 min
        await BackdateSessionAsync(TimeSpan.FromMinutes(10));              // 50 min left

        using (var restarted = NewDb())
            await ServiceFor(restarted).RestoreActiveSessionsAsync();

        Assert.Equal(SessionStatus.Active, (await GetOnlySessionAsync()).Status);
        Assert.Equal(PcState.PrepaySession, (await GetPcAsync(Pc1)).State);
    }

    [Fact]
    public async Task Restart_keeps_a_postpay_session_running_because_the_customer_kept_playing()
    {
        using (var before = NewDb())
            await ServiceFor(before).StartPostpayAsync(Pc1);
        await BackdateSessionAsync(TimeSpan.FromHours(3));

        using (var restarted = NewDb())
            await ServiceFor(restarted).RestoreActiveSessionsAsync();

        var session = await GetOnlySessionAsync();
        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.Null(session.ActualEndUtc);
        Assert.Equal(PcState.PostpaySession, (await GetPcAsync(Pc1)).State);
    }

    [Fact]
    public async Task The_expiry_tick_ends_a_prepay_session_whose_time_has_run_out()
    {
        using (var before = NewDb())
            await ServiceFor(before).StartPrepayByMoneyAsync(Pc1, 5_000); // 30 min
        await BackdateSessionAsync(TimeSpan.FromMinutes(31));

        using var db = NewDb();
        await ServiceFor(db).EndExpiredPrepaySessionsAsync();

        Assert.Equal(SessionStatus.Ended, (await GetOnlySessionAsync()).Status);
        Assert.Equal(PcState.Locked, (await GetPcAsync(Pc1)).State);
    }
}
