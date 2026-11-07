using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>The bar's reports: one per working shift (smena), kept for a month.</summary>
public class BarReportTests : SessionTestBase
{
    private static BarService BarFor(ClubDbContext db) => new(db, ServiceFor(db));

    private async Task<int> NewColaAsync(int stock = 100)
    {
        using var db = NewDb();
        return (await BarFor(db).CreateProductAsync("Cola", 5_000, stock)).Id;
    }

    private async Task CloseShiftAsync()
    {
        using var db = NewDb();
        await ShiftHelper.CloseAsync(db);
    }

    [Fact]
    public async Task A_sale_belongs_to_the_open_shift_and_opens_one_when_none_is_open()
    {
        var cola = await NewColaAsync();
        using (var db = NewDb())
            Assert.Null(await ShiftHelper.GetOpenAsync(db));

        using (var db = NewDb())
            await BarFor(db).SellAsync(new BarService.SaleRequest(cola, 1));

        using var check = NewDb();
        var shift = await ShiftHelper.GetOpenAsync(check);
        Assert.NotNull(shift);
        Assert.Equal(shift!.Id, (await check.BarSales.SingleAsync()).ShiftId);
        Assert.Contains(await check.EventLogEntries.ToListAsync(), e => e.Message.Contains("opened automatically"));
    }

    [Fact]
    public async Task Each_shift_has_its_own_income_credit_and_products_and_a_debt_counts_in_the_shift_it_is_paid()
    {
        var cola = await NewColaAsync();
        int debtId;

        // Shift 1: a counter sale, a sale on a PC's bill, a sale on credit, and a cancelled one.
        using (var db = NewDb())
        {
            var bar = BarFor(db);
            await ShiftHelper.OpenAsync(db);
            await ServiceFor(db).StartPostpayAsync(Pc1);
            await bar.SellAsync(new BarService.SaleRequest(cola, 2));                                             // counter: 10 000
            await bar.SellAsync(new BarService.SaleRequest(cola, 1, SessionMachineGuid: Pc1));                    // on a bill: 5 000
            debtId = (await bar.SellAsync(new BarService.SaleRequest(cola, 3, Unpaid: true, Comment: "Aziz"))).Id; // credit: 15 000
            var cancelled = await bar.SellAsync(new BarService.SaleRequest(cola, 4));
            await bar.VoidSaleAsync(cancelled.Id);
            await ServiceFor(db).EndSessionEarlyAsync(Pc1);
        }
        await CloseShiftAsync();

        // Shift 2: Aziz pays his debt, and one more counter sale.
        using (var db = NewDb())
        {
            await ShiftHelper.OpenAsync(db);
            await BarFor(db).MarkPaidAsync(debtId);
            await BarFor(db).SellAsync(new BarService.SaleRequest(cola, 1));                                      // counter: 5 000
        }

        using var check = NewDb();
        var history = await new ReportsService(check).GetBarShiftHistoryAsync(DateTime.UtcNow);
        Assert.Equal(2, history.Count);
        var second = history[0]; // newest first
        var first = history[1];

        Assert.Equal(10_000, first.CounterPaidUzs);
        Assert.Equal(5_000, first.SessionItemsUzs);
        Assert.Equal(0, first.DebtsPaidUzs);            // not paid yet in shift 1: not income there
        Assert.Equal(15_000, first.CreditGivenUzs);
        Assert.Equal(15_000, first.TotalIncomeUzs);
        Assert.Equal(6, first.UnitsSold);               // 2 + 1 + 3, the cancelled 4 not counted
        var cola1 = Assert.Single(first.ByProduct);
        Assert.Equal(("Cola", 6, 30_000L), (cola1.ProductName, cola1.Units, cola1.AmountUzs));

        Assert.Equal(5_000, second.CounterPaidUzs);
        Assert.Equal(15_000, second.DebtsPaidUzs);      // the debt is income of the shift it was paid in
        Assert.Equal(0, second.CreditGivenUzs);
        Assert.Equal(20_000, second.TotalIncomeUzs);
        Assert.Equal(1, second.UnitsSold);

        Assert.Equal(0, await new ReportsService(check).GetUnpaidNowAsync());
    }

    [Fact]
    public async Task What_is_still_owed_is_reported_whatever_shift_it_came_from()
    {
        var cola = await NewColaAsync();
        using (var db = NewDb())
        {
            await ShiftHelper.OpenAsync(db);
            await BarFor(db).SellAsync(new BarService.SaleRequest(cola, 2, Unpaid: true, Comment: "Bobur"));
        }
        await CloseShiftAsync();

        using var check = NewDb();
        Assert.Equal(10_000, await new ReportsService(check).GetUnpaidNowAsync());
    }

    [Fact]
    public async Task The_history_lists_the_last_month_of_shifts_and_the_open_one_but_deletes_nothing()
    {
        var cola = await NewColaAsync();
        using (var db = NewDb())
        {
            await ShiftHelper.OpenAsync(db);
            await BarFor(db).SellAsync(new BarService.SaleRequest(cola, 1));
        }
        await CloseShiftAsync();
        using (var db = NewDb())
        {
            await ShiftHelper.OpenAsync(db);
            await BarFor(db).SellAsync(new BarService.SaleRequest(cola, 1));
        }

        // Make the first shift 40 days old.
        using (var db = NewDb())
        {
            var old = await db.Shifts.OrderBy(s => s.Id).FirstAsync();
            old.OpenedUtc = DateTime.UtcNow.AddDays(-40);
            old.ClosedUtc = DateTime.UtcNow.AddDays(-40).AddHours(8);
            await db.SaveChangesAsync();
        }

        using var check = NewDb();
        var history = await new ReportsService(check).GetBarShiftHistoryAsync(DateTime.UtcNow);
        var shown = Assert.Single(history);
        Assert.Null(shown.ClosedUtc); // the open shift
        Assert.Equal(2, await check.BarSales.CountAsync()); // the old sale is still in the database
    }

    [Fact]
    public async Task A_shift_with_only_bar_sales_is_not_closed_as_idle_while_the_bar_is_busy()
    {
        var cola = await NewColaAsync();
        using (var db = NewDb())
        {
            await ShiftHelper.OpenAsync(db);
            await BarFor(db).SellAsync(new BarService.SaleRequest(cola, 1));
        }

        // The shift opened 10 hours ago, but a drink was sold 1 hour ago.
        using (var db = NewDb())
        {
            var shift = await db.Shifts.SingleAsync();
            shift.OpenedUtc = DateTime.UtcNow.AddHours(-10);
            (await db.BarSales.SingleAsync()).CreatedUtc = DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }

        using (var db = NewDb())
            Assert.Null(await ShiftHelper.CloseIfIdleAsync(db, DateTime.UtcNow));

        // Nothing for 9 hours after the last sale: now it is closed, at the time of that sale.
        using (var db = NewDb())
        {
            var closed = await ShiftHelper.CloseIfIdleAsync(db, DateTime.UtcNow.AddHours(9));
            Assert.NotNull(closed);
            Assert.InRange(closed!.ClosedUtc!.Value, DateTime.UtcNow.AddHours(-1.1), DateTime.UtcNow.AddHours(-0.9));
        }
    }
}
