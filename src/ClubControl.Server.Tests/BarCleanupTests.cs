using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>
/// The bar keeps one month of history; older sales and their stock-log lines are removed. The bar no longer
/// writes stock-log lines, but databases from older versions still hold them, so the tests add such lines by hand.
/// </summary>
public class BarCleanupTests : SessionTestBase
{
    private static BarService BarFor(ClubDbContext db) => new(db, ServiceFor(db));

    /// <summary>Adds a stock-log line like the ones older versions wrote, one per sale (null = a manual change not tied to a sale).</summary>
    private async Task AddLegacyLinesAsync(int productId, params int?[] saleIds)
    {
        using var db = NewDb();
        foreach (var saleId in saleIds)
            db.BarStockMovements.Add(new BarStockMovement { BarProductId = productId, BarSaleId = saleId, Type = BarStockMovementType.Sale, Delta = -1 });
        await db.SaveChangesAsync();
    }

    /// <summary>Moves a sale, and the stock-log lines that belong to it, back in time.</summary>
    private async Task AgeAsync(int saleId, int days, bool alsoPaid = false, bool alsoVoided = false)
    {
        using var db = NewDb();
        var shift = TimeSpan.FromDays(days);
        var sale = await db.BarSales.SingleAsync(s => s.Id == saleId);
        sale.CreatedUtc -= shift;
        if (alsoPaid && sale.PaidUtc.HasValue) sale.PaidUtc -= shift;
        if (alsoVoided && sale.VoidedUtc.HasValue) sale.VoidedUtc -= shift;
        foreach (var m in await db.BarStockMovements.Where(m => m.BarSaleId == saleId).ToListAsync())
            m.CreatedUtc -= shift;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Old_sales_and_their_stock_lines_go_but_recent_ones_and_unpaid_ones_stay()
    {
        int old, oldVoided, oldDebtPaidLongAgo, oldDebtPaidRecently, oldDebtStillOwed, recent, productId;
        using (var db = NewDb())
        {
            var bar = BarFor(db);
            productId = (await bar.CreateProductAsync("Cola", 5_000)).Id;
            old = (await bar.SellAsync(new BarService.SaleRequest(productId, 1))).Id;
            oldVoided = (await bar.SellAsync(new BarService.SaleRequest(productId, 1))).Id;
            await bar.VoidSaleAsync(oldVoided);
            oldDebtPaidLongAgo = (await bar.SellAsync(new BarService.SaleRequest(productId, 1, Unpaid: true, Comment: "A"))).Id;
            await bar.MarkPaidAsync(oldDebtPaidLongAgo);
            oldDebtPaidRecently = (await bar.SellAsync(new BarService.SaleRequest(productId, 1, Unpaid: true, Comment: "B"))).Id;
            oldDebtStillOwed = (await bar.SellAsync(new BarService.SaleRequest(productId, 1, Unpaid: true, Comment: "C"))).Id;
            await bar.MarkPaidAsync(oldDebtPaidRecently);
            recent = (await bar.SellAsync(new BarService.SaleRequest(productId, 1))).Id;
        }

        await AddLegacyLinesAsync(productId, old, oldVoided, oldDebtPaidLongAgo, oldDebtPaidRecently, oldDebtStillOwed, recent);

        await AgeAsync(old, 40);
        await AgeAsync(oldVoided, 40, alsoVoided: true);
        await AgeAsync(oldDebtPaidLongAgo, 40, alsoPaid: true);
        await AgeAsync(oldDebtPaidRecently, 40);   // sold long ago, but paid just now: its money is this month's
        await AgeAsync(oldDebtStillOwed, 60);      // still owed: never deleted
        await AgeAsync(recent, 5);

        using (var db = NewDb())
        {
            var result = await BarCleanupService.PurgeOldAsync(db, DateTime.UtcNow);
            Assert.Equal(3, result.Sales); // old, oldVoided, oldDebtPaidLongAgo
        }

        using var check = NewDb();
        var left = await check.BarSales.OrderBy(s => s.Id).Select(s => s.Id).ToListAsync();
        Assert.Equal(new[] { oldDebtPaidRecently, oldDebtStillOwed, recent }, left);

        // No stock-log line is left pointing at a deleted sale, and the kept sales keep theirs.
        var lines = await check.BarStockMovements.ToListAsync();
        Assert.All(lines.Where(l => l.BarSaleId.HasValue), l => Assert.Contains(l.BarSaleId!.Value, left));
        Assert.Equal(3, lines.Count(l => l.BarSaleId.HasValue));
        Assert.Contains(await check.EventLogEntries.ToListAsync(), e => e.Message.Contains("history older than"));

        // What is owed is still reported, and the bar still sells.
        Assert.Equal(oldDebtStillOwed, Assert.Single(await BarFor(check).GetUnpaidAsync()).Id);
        await BarFor(check).SellAsync(new BarService.SaleRequest(productId, 1));
    }

    [Fact]
    public async Task Old_manual_stock_lines_are_removed_too_and_a_second_run_changes_nothing()
    {
        int productId;
        using (var db = NewDb())
        {
            productId = (await BarFor(db).CreateProductAsync("Cola", 5_000)).Id;
        }
        await AddLegacyLinesAsync(productId, null, null);
        using (var db = NewDb())
        {
            foreach (var m in await db.BarStockMovements.ToListAsync())
                m.CreatedUtc = DateTime.UtcNow.AddDays(-45);
            await db.SaveChangesAsync();
        }

        using (var db = NewDb())
            Assert.Equal(new BarCleanupService.Result(0, 2), await BarCleanupService.PurgeOldAsync(db, DateTime.UtcNow));
        using (var db = NewDb())
            Assert.Equal(new BarCleanupService.Result(0, 0), await BarCleanupService.PurgeOldAsync(db, DateTime.UtcNow));

        using var check = NewDb();
        Assert.Empty(check.BarStockMovements);
    }
}
