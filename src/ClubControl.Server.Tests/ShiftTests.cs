using ClubControl.Server.Data;
using ClubControl.Server.Services;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>Working shifts: open/close rules, sessions attached to the open shift, and the per-shift report.</summary>
public class ShiftTests : SessionTestBase
{
    [Fact]
    public async Task Starting_a_session_with_no_open_shift_opens_one_and_attaches_the_session()
    {
        using (var db = NewDb())
            await ServiceFor(db).StartPostpayAsync(Pc1);

        using var check = NewDb();
        var shift = await check.Shifts.SingleAsync();
        Assert.Null(shift.ClosedUtc);
        Assert.Equal(shift.Id, (await check.Sessions.SingleAsync()).ShiftId);
        Assert.Contains(await check.EventLogEntries.ToListAsync(), e => e.Type == EventType.ShiftOpened);
    }

    [Fact]
    public async Task Sessions_started_while_a_shift_is_open_join_that_shift()
    {
        using (var db = NewDb())
            await ShiftHelper.OpenAsync(db);

        using (var db = NewDb())
        {
            await ServiceFor(db).StartPostpayAsync(Pc1);
            await ServiceFor(db).StartPrepayByMoneyAsync(Pc2, 5_000);
        }

        using var check = NewDb();
        var shift = await check.Shifts.SingleAsync();
        Assert.All(await check.Sessions.ToListAsync(), s => Assert.Equal(shift.Id, s.ShiftId));
    }

    [Fact]
    public async Task Opening_a_shift_while_one_is_open_is_refused()
    {
        using var db = NewDb();
        await ShiftHelper.OpenAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ShiftHelper.OpenAsync(db));
        Assert.Equal(1, await db.Shifts.CountAsync());
    }

    [Fact]
    public async Task A_shift_cannot_be_closed_while_a_session_is_running()
    {
        using (var db = NewDb())
            await ServiceFor(db).StartPostpayAsync(Pc1);

        using (var db = NewDb())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => ShiftHelper.CloseAsync(db));
            Assert.Equal(1, await ShiftHelper.CountRunningSessionsAsync(db));
        }

        using var check = NewDb();
        Assert.Null((await check.Shifts.SingleAsync()).ClosedUtc);
    }

    [Fact]
    public async Task A_shift_closes_once_every_session_has_ended_and_the_next_session_starts_a_new_shift()
    {
        using (var db = NewDb())
            await ServiceFor(db).StartPostpayAsync(Pc1);
        using (var db = NewDb())
            await ServiceFor(db).EndSessionEarlyAsync(Pc1);

        using (var db = NewDb())
            await ShiftHelper.CloseAsync(db);

        using (var db = NewDb())
        {
            Assert.Null(await ShiftHelper.GetOpenAsync(db));
            await ServiceFor(db).StartPostpayAsync(Pc2);
        }

        using var check = NewDb();
        var shifts = await check.Shifts.OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, shifts.Count);
        Assert.NotNull(shifts[0].ClosedUtc);
        Assert.Null(shifts[1].ClosedUtc);
        Assert.Contains(await check.EventLogEntries.ToListAsync(), e => e.Type == EventType.ShiftClosed);
    }

    // ---- a forgotten shift closes itself ----

    /// <summary>Makes the open shift and everything in it look <paramref name="age"/> older.</summary>
    private async Task AgeEverythingAsync(TimeSpan age)
    {
        using var db = NewDb();
        foreach (var shift in await db.Shifts.ToListAsync())
            shift.OpenedUtc -= age;
        foreach (var session in await db.Sessions.ToListAsync())
        {
            session.StartUtc -= age;
            session.ActualEndUtc -= age;
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_shift_with_no_activity_for_eight_hours_is_closed_at_its_last_activity()
    {
        using (var db = NewDb())
            await ServiceFor(db).StartPostpayAsync(Pc1);
        using (var db = NewDb())
            await ServiceFor(db).EndSessionEarlyAsync(Pc1);
        await AgeEverythingAsync(TimeSpan.FromHours(9));

        using (var db = NewDb())
            Assert.NotNull(await ShiftHelper.CloseIfIdleAsync(db, DateTime.UtcNow));

        using var check = NewDb();
        var shift = await check.Shifts.SingleAsync();
        var sessionEnd = (await check.Sessions.SingleAsync()).ActualEndUtc!.Value;
        Assert.Equal(sessionEnd, shift.ClosedUtc); // closed when the last session ended, not 9 hours later
        Assert.Contains(await check.EventLogEntries.ToListAsync(), e => e.Type == EventType.ShiftClosed && e.Message.Contains("automatically"));
    }

    [Fact]
    public async Task A_shift_with_recent_activity_stays_open_even_if_it_was_opened_long_ago()
    {
        using (var db = NewDb())
            await ServiceFor(db).StartPostpayAsync(Pc1);
        using (var db = NewDb())
            await ServiceFor(db).EndSessionEarlyAsync(Pc1);
        await AgeEverythingAsync(TimeSpan.FromHours(9));

        // The session ended 9 hours ago; a newer one ended 2 hours ago.
        using (var db = NewDb())
            await ServiceFor(db).StartPostpayAsync(Pc2);
        using (var db = NewDb())
            await ServiceFor(db).EndSessionEarlyAsync(Pc2);

        using (var db = NewDb())
            Assert.Null(await ShiftHelper.CloseIfIdleAsync(db, DateTime.UtcNow.AddHours(2)));

        using var check = NewDb();
        Assert.Null((await check.Shifts.SingleAsync()).ClosedUtc);
    }

    [Fact]
    public async Task An_idle_shift_is_not_closed_while_a_session_is_still_running()
    {
        using (var db = NewDb())
            await ServiceFor(db).StartPostpayAsync(Pc1);
        await AgeEverythingAsync(TimeSpan.FromHours(9));

        using (var db = NewDb())
            Assert.Null(await ShiftHelper.CloseIfIdleAsync(db, DateTime.UtcNow));

        using var check = NewDb();
        Assert.Null((await check.Shifts.SingleAsync()).ClosedUtc);
    }

    [Fact]
    public async Task A_shift_opened_by_hand_and_never_used_is_closed_after_eight_hours()
    {
        using (var db = NewDb())
            await ShiftHelper.OpenAsync(db);

        using (var db = NewDb())
            Assert.Null(await ShiftHelper.CloseIfIdleAsync(db, DateTime.UtcNow.AddHours(7)));
        using (var db = NewDb())
            Assert.NotNull(await ShiftHelper.CloseIfIdleAsync(db, DateTime.UtcNow.AddHours(9)));
    }

    [Fact]
    public async Task Closing_when_no_shift_is_open_is_refused()
    {
        using var db = NewDb();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ShiftHelper.CloseAsync(db));
    }

    [Fact]
    public async Task Shift_report_holds_exactly_that_shifts_sessions_even_when_they_span_midnight()
    {
        // First shift: one session that started 30 hours ago and one that started a minute ago - far
        // more than a calendar day apart, but the same shift.
        using (var db = NewDb())
        {
            await ServiceFor(db).StartPrepayByMoneyAsync(Pc1, 3_000);
            await ServiceFor(db).StartPrepayByMoneyAsync(Pc2, 5_000);
        }

        using (var db = NewDb())
        {
            var early = await db.Sessions.Include(s => s.Segments).FirstAsync(s => s.AmountUzs == 3_000);
            early.StartUtc -= TimeSpan.FromHours(30);
            foreach (var segment in early.Segments)
                segment.StartUtc -= TimeSpan.FromHours(30);
            foreach (var session in await db.Sessions.ToListAsync())
            {
                session.Status = SessionStatus.Ended;
                session.ActualEndUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            await ShiftHelper.CloseAsync(db);
        }

        // Second shift: a later session that must not leak into the first.
        using (var db = NewDb())
            await ServiceFor(db).StartPrepayByMoneyAsync(VipPc, 7_000);

        using var reportDb = NewDb();
        var reports = new ReportsService(reportDb);
        var shifts = await reports.GetShiftsAsync();
        Assert.Equal(2, shifts.Count);

        var first = await reports.BuildShiftReportAsync(shifts[1].Id, ResourceKind.Computer);
        Assert.Equal(2, first.Totals.SessionCount);
        Assert.Equal(8_000, first.Totals.TotalIncomeUzs);

        var second = await reports.BuildShiftReportAsync(shifts[0].Id, ResourceKind.Computer);
        Assert.Equal(7_000, Assert.Single(second.Sessions).AmountUzs);

        // The calendar day that has the 30-hours-old session misses it - the reason shifts exist.
        var today = await reports.BuildReportAsync(DateTime.UtcNow.AddHours(-24), DateTime.UtcNow.AddHours(1), ResourceKind.Computer);
        Assert.Equal(2, today.Totals.SessionCount); // the 5 000 session of shift 1 and the 7 000 one of shift 2
    }

    [Fact]
    public async Task A_session_from_before_shifts_existed_is_in_no_shift_report()
    {
        using (var db = NewDb())
        {
            await ServiceFor(db).StartPostpayAsync(Pc1);
            var session = await db.Sessions.SingleAsync();
            session.ShiftId = null; // as if it was recorded before the shift feature
            await db.SaveChangesAsync();
        }

        using var reportDb = NewDb();
        var shift = Assert.Single(await new ReportsService(reportDb).GetShiftsAsync());
        var report = await new ReportsService(reportDb).BuildShiftReportAsync(shift.Id, ResourceKind.Computer);
        Assert.Empty(report.Sessions);
    }
}
