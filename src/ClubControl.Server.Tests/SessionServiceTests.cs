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
    public async Task Stopping_a_prepay_session_early_gives_no_refund()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPrepayByMoneyAsync(Pc1, 10_000);

        await service.EndSessionEarlyAsync(Pc1);

        var session = await GetOnlySessionAsync();
        Assert.Equal(SessionStatus.Ended, session.Status);
        Assert.Equal(10_000, session.AmountUzs);
        Assert.NotNull(session.ActualEndUtc);
        Assert.All(session.Segments, s => Assert.NotNull(s.EndUtc));
        Assert.Equal(PcState.Locked, (await GetPcAsync(Pc1)).State);
    }

    // ---- extend ----

    [Fact]
    public async Task ExtendByMoney_adds_time_and_money_at_the_current_price()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPrepayByMoneyAsync(Pc1, 10_000);
        var endBefore = (await GetOnlySessionAsync()).PlannedEndUtc!.Value;

        await service.ExtendPrepayByMoneyAsync(Pc1, 5_000);

        var session = await GetOnlySessionAsync();
        Assert.Equal(15_000, session.AmountUzs);
        Assert.Equal(30, (session.PlannedEndUtc!.Value - endBefore).TotalMinutes, precision: 3);
    }

    [Fact]
    public async Task ExtendByTime_adds_the_price_of_those_minutes()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPrepayByMoneyAsync(Pc1, 10_000);

        await service.ExtendPrepayByTimeAsync(Pc1, 30);

        Assert.Equal(15_000, (await GetOnlySessionAsync()).AmountUzs);
    }

    [Fact]
    public async Task Extending_past_the_tariff_maximum_is_rejected_and_changes_nothing()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPrepayByMoneyAsync(Pc1, 45_000);
        var before = await GetOnlySessionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExtendPrepayByMoneyAsync(Pc1, 6_000));

        var after = await GetOnlySessionAsync();
        Assert.Equal(45_000, after.AmountUzs);
        Assert.Equal(before.PlannedEndUtc, after.PlannedEndUtc);
    }

    [Fact]
    public async Task Extending_by_too_little_money_to_buy_a_minute_is_rejected()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPrepayByMoneyAsync(Pc1, 10_000);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExtendPrepayByMoneyAsync(Pc1, 100));

        Assert.Equal(10_000, (await GetOnlySessionAsync()).AmountUzs);
    }

    [Fact]
    public async Task Extending_a_pc_without_a_prepay_session_is_rejected()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPostpayAsync(Pc1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExtendPrepayByMoneyAsync(Pc1, 5_000));
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
    public async Task Free_time_is_recorded_as_free_with_a_zero_amount()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartFreeAsync(Pc1, "tournament winner");

        var session = await GetOnlySessionAsync();
        Assert.True(session.IsFree);
        Assert.Equal(SessionType.Free, session.Type);
        Assert.Equal("tournament winner", session.Note);

        await service.EndSessionEarlyAsync(Pc1);

        Assert.Equal(0, (await GetOnlySessionAsync()).AmountUzs);
    }

    [Fact]
    public async Task Ending_a_pc_with_no_session_is_rejected()
    {
        using var db = NewDb();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ServiceFor(db).EndSessionEarlyAsync(Pc1));
    }

    // ---- move ----

    [Fact]
    public async Task Moving_a_prepay_session_converts_the_remaining_money_at_the_new_price()
    {
        using (var before = NewDb())
            await ServiceFor(before).StartPrepayByMoneyAsync(Pc1, 10_000); // 60 min at 10 000/h
        await BackdateSessionAsync(TimeSpan.FromMinutes(30));              // 30 min left = 5 000 UZS

        using var db = NewDb();
        await ServiceFor(db).MoveSessionAsync(Pc1, VipPc);

        // 5 000 UZS at 15 000/h is 20 minutes (19 if a fraction of a second has already passed).
        var session = await GetOnlySessionAsync();
        var minutesLeft = (session.PlannedEndUtc!.Value - DateTime.UtcNow).TotalMinutes;
        Assert.InRange(minutesLeft, 18.9, 20.1);
        Assert.Equal(10_000, session.AmountUzs);

        Assert.Equal(2, session.Segments.Count);
        var closed = session.Segments.Single(s => s.EndUtc != null);
        var open = session.Segments.Single(s => s.EndUtc == null);
        Assert.Equal(StandardPrice, closed.PricePerHourUzs);
        Assert.Equal(VipPrice, open.PricePerHourUzs);

        Assert.Equal(PcState.Locked, (await GetPcAsync(Pc1)).State);
        Assert.Equal(PcState.PrepaySession, (await GetPcAsync(VipPc)).State);
        Assert.Equal((await GetPcAsync(VipPc)).Id, session.PcId);
    }

    [Fact]
    public async Task Moving_a_postpay_session_keeps_the_cost_so_far()
    {
        await StartPostpayThenBackdateAsync(Pc1, AlmostThirtyMinutes); // 5 000 UZS at the old price

        using (var db = NewDb())
            await ServiceFor(db).MoveSessionAsync(Pc1, VipPc);
        using var db2 = NewDb();
        await ServiceFor(db2).EndSessionEarlyAsync(VipPc);

        // Nothing has been added at the new price yet, and nothing already run up was lost.
        Assert.Equal(5_000, (await GetOnlySessionAsync()).AmountUzs);
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
