using ClubControl.Server.Data.Models;
using ClubControl.Server.Services;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>Items (drinks, snacks) added to a running session and paid on top of the time charge.</summary>
public class ExtrasTests : SessionTestBase
{
    [Fact]
    public async Task An_item_added_mid_session_is_listed_and_logged()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPostpayAsync(Pc1);

        await service.AddExtraAsync(Pc1, "  Cola ", 5_000);
        await service.AddExtraAsync(Pc1, "Chips", 3_000);

        var items = await service.GetExtrasAsync(Pc1);
        Assert.Equal(new[] { "Cola", "Chips" }, items.Select(i => i.Description));
        Assert.Equal(8_000, items.Sum(i => i.AmountUzs));

        var snapshot = await service.GetActiveSessionSnapshotAsync(Pc1);
        Assert.Equal(8_000, snapshot!.ExtrasUzs);
        Assert.Contains(await db.EventLogEntries.ToListAsync(), e => e.Type == EventType.ItemAdded && e.Message.Contains("Cola"));
    }

    [Fact]
    public async Task Items_do_not_change_the_time_charge_or_the_prepaid_time()
    {
        using (var db = NewDb())
        {
            var service = ServiceFor(db);
            await service.StartPrepayByMoneyAsync(Pc1, 10_000);
            await service.StartPostpayAsync(Pc2);
            await service.AddExtraAsync(Pc1, "Cola", 5_000);
            await service.AddExtraAsync(Pc2, "Cola", 5_000);
        }

        using (var db = NewDb())
        {
            await ServiceFor(db).EndSessionEarlyAsync(Pc1);
            await ServiceFor(db).EndSessionEarlyAsync(Pc2);
        }

        using var check = NewDb();
        var sessions = await check.Sessions.Include(s => s.Extras).Include(s => s.Pc).ToListAsync();
        Assert.Equal(10_000, sessions.Single(s => s.Type == SessionType.Prepay).AmountUzs);          // still just what was prepaid
        Assert.Equal(StandardMin, sessions.Single(s => s.Type == SessionType.Postpay).AmountUzs);    // minimum charge, items not added in
        Assert.All(sessions, s => Assert.Equal(5_000, s.Extras.Sum(x => x.AmountUzs)));              // items kept for the record
    }

    [Fact]
    public async Task An_item_needs_a_name_a_positive_price_and_a_running_session()
    {
        using var db = NewDb();
        var service = ServiceFor(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExtraAsync(Pc1, "Cola", 5_000)); // no session yet

        await service.StartPostpayAsync(Pc1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExtraAsync(Pc1, "   ", 5_000));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExtraAsync(Pc1, "Cola", 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExtraAsync(Pc1, "Cola", -100));
        Assert.Empty(await service.GetExtrasAsync(Pc1));
    }

    [Fact]
    public async Task A_mistaken_item_can_be_removed_only_while_the_session_runs()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPostpayAsync(Pc1);
        await service.AddExtraAsync(Pc1, "Cola", 5_000);
        await service.AddExtraAsync(Pc1, "Tea", 2_000);

        var cola = (await service.GetExtrasAsync(Pc1)).Single(i => i.Description == "Cola");
        await service.RemoveExtraAsync(cola.Id);
        Assert.Equal(new[] { "Tea" }, (await service.GetExtrasAsync(Pc1)).Select(i => i.Description));

        var tea = (await service.GetExtrasAsync(Pc1)).Single();
        await service.EndSessionEarlyAsync(Pc1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveExtraAsync(tea.Id));
    }

    [Fact]
    public async Task Items_follow_the_session_when_it_is_moved()
    {
        using var db = NewDb();
        var service = ServiceFor(db);
        await service.StartPostpayAsync(Pc1);
        await service.AddExtraAsync(Pc1, "Cola", 5_000);

        await service.MoveSessionAsync(Pc1, Pc2);

        Assert.Empty(await service.GetExtrasAsync(Pc1));
        Assert.Equal(5_000, (await service.GetExtrasAsync(Pc2)).Single().AmountUzs);
    }

    [Fact]
    public async Task The_report_totals_the_items_separately_from_session_income()
    {
        using (var db = NewDb())
        {
            var service = ServiceFor(db);
            await service.StartPrepayByMoneyAsync(Pc1, 10_000);
            await service.AddExtraAsync(Pc1, "Cola", 5_000);
            await service.AddExtraAsync(Pc1, "Chips", 3_000);
        }

        using var reportDb = NewDb();
        var report = await new ReportsService(reportDb).BuildReportAsync(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1), ResourceKind.Computer);

        Assert.Equal(8_000, report.Totals.ExtrasUzs);
        Assert.Equal(10_000, report.Totals.TotalIncomeUzs);
    }
}
