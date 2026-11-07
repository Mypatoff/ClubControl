using ClubControl.Server.Data.Models;
using ClubControl.Server.Services;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>The background ticks read only what they need and tell clients less often, without changing what is billed.</summary>
public class MonitorLoadTests : SessionTestBase
{
    private static readonly DateTime T0 = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_session_is_sent_once_and_then_only_every_30_seconds()
    {
        var throttle = new PostpayUpdateThrottle();

        Assert.True(throttle.ShouldSend(1, T0));
        Assert.False(throttle.ShouldSend(1, T0.AddSeconds(2)));
        Assert.False(throttle.ShouldSend(1, T0.AddSeconds(29)));
        Assert.True(throttle.ShouldSend(1, T0.AddSeconds(30)));
        Assert.True(throttle.ShouldSend(2, T0.AddSeconds(31)), "another session has its own schedule");
    }

    [Fact]
    public async Task The_throttle_is_asked_once_per_running_postpay_session_and_a_no_answer_sends_nothing()
    {
        await Start_postpay_on_Pc1_and_Pc2();
        var asked = new List<int>();

        using var db = NewDb();
        await ServiceFor(db).TickPostpayCostsAsync((sessionId, _) => { asked.Add(sessionId); return false; });

        Assert.Equal(2, asked.Count);
        Assert.Equal(2, asked.Distinct().Count());
    }

    [Fact]
    public async Task A_postpay_session_at_the_tariff_maximum_is_locked_even_when_no_update_is_due()
    {
        await StartPostpayAsync(Pc1);
        await BackdateSessionAsync(TimeSpan.FromHours(10)); // far past the 50 000 UZS maximum

        using (var db = NewDb())
            await ServiceFor(db).TickPostpayCostsAsync((_, _) => false);

        Assert.Equal(PcState.LimitReached, (await GetPcAsync(Pc1)).State);
    }

    [Fact]
    public async Task Only_prepay_sessions_whose_time_is_up_are_ended()
    {
        using (var db = NewDb())
        {
            await ServiceFor(db).StartPrepayByTimeAsync(Pc1, 30);
            await ServiceFor(db).StartPrepayByTimeAsync(Pc2, 30);
        }

        using (var db = NewDb()) // Pc1's 30 minutes ran out a minute ago, Pc2's have 29 minutes left
        {
            var first = await db.Sessions.Include(s => s.Segments).FirstAsync(s => s.Pc!.MachineGuid == Pc1);
            first.StartUtc -= TimeSpan.FromMinutes(31);
            first.PlannedEndUtc -= TimeSpan.FromMinutes(31);
            foreach (var segment in first.Segments)
                segment.StartUtc -= TimeSpan.FromMinutes(31);
            await db.SaveChangesAsync();
        }

        using (var db = NewDb())
            await ServiceFor(db).EndExpiredPrepaySessionsAsync();

        Assert.Equal(PcState.Locked, (await GetPcAsync(Pc1)).State);
        Assert.Equal(PcState.PrepaySession, (await GetPcAsync(Pc2)).State);
    }

    private async Task StartPostpayAsync(Guid machineGuid)
    {
        using var db = NewDb();
        await ServiceFor(db).StartPostpayAsync(machineGuid);
    }

    private async Task Start_postpay_on_Pc1_and_Pc2()
    {
        await StartPostpayAsync(Pc1);
        await StartPostpayAsync(Pc2);
    }
}
