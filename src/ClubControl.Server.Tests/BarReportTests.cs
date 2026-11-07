using ClubControl.Server.Data;
using ClubControl.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>The bar's part of the reports: what counts as income, and when.</summary>
public class BarReportTests : SessionTestBase
{
    private static BarService BarFor(ClubDbContext db) => new(db, ServiceFor(db));

    [Fact]
    public async Task Income_splits_into_counter_bills_and_paid_debts_and_ignores_cancelled_sales()
    {
        using var db = NewDb();
        var bar = BarFor(db);
        var cola = (await bar.CreateProductAsync("Cola", 5_000, 100)).Id;
        await ServiceFor(db).StartPostpayAsync(Pc1);

        await bar.SellAsync(new BarService.SaleRequest(cola, 2));                                           // counter, paid: 10 000
        await bar.SellAsync(new BarService.SaleRequest(cola, 1, SessionMachineGuid: Pc1));                  // on a PC's bill: 5 000
        await bar.SellAsync(new BarService.SaleRequest(cola, 3, Unpaid: true, Comment: "Aziz"));            // still owed: 15 000
        var paidLater = await bar.SellAsync(new BarService.SaleRequest(cola, 1, Unpaid: true, Comment: "Bobur"));
        await bar.MarkPaidAsync(paidLater.Id);                                                              // debt paid: 5 000
        var cancelled = await bar.SellAsync(new BarService.SaleRequest(cola, 4));                           // cancelled: counts for nothing
        await bar.VoidSaleAsync(cancelled.Id);

        using var check = NewDb();
        var reports = new ReportsService(check);
        var report = await reports.BuildBarReportAsync(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

        Assert.Equal(10_000, report.CounterPaidUzs);
        Assert.Equal(5_000, report.SessionItemsUzs);
        Assert.Equal(5_000, report.DebtsPaidUzs);
        Assert.Equal(20_000, report.TotalIncomeUzs);
        Assert.Equal(15_000, report.UnpaidNowUzs);
        Assert.Equal(4, report.UnitsSold); // 2 + 1 + 1: the 3 still owed are not income yet
        var row = Assert.Single(report.ByProduct);
        Assert.Equal(("Cola", 4, 20_000L), (row.ProductName, row.Units, row.IncomeUzs));

        // A period with nothing in it is empty, but what is still owed does not depend on the period.
        var later = await reports.BuildBarReportAsync(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2));
        Assert.Equal(0, later.TotalIncomeUzs);
        Assert.Empty(later.ByProduct);
        Assert.Equal(15_000, later.UnpaidNowUzs);
    }
}
