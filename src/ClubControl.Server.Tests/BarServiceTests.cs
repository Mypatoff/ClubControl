using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Services;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>The bar: products and stock, selling at the counter or to a PC, unpaid sales, cancelling.</summary>
public class BarServiceTests : SessionTestBase
{
    private static BarService BarFor(ClubDbContext db) => new(db, ServiceFor(db));

    private async Task<int> NewProductAsync(string name = "Cola", int price = 5_000, int stock = 10)
    {
        using var db = NewDb();
        return (await BarFor(db).CreateProductAsync(name, price, stock)).Id;
    }

    [Fact]
    public async Task A_sale_lowers_stock_keeps_the_price_and_is_logged()
    {
        var id = await NewProductAsync();

        using (var db = NewDb())
        {
            var sale = await BarFor(db).SellAsync(new BarService.SaleRequest(id, 3));
            Assert.Equal(15_000, sale.TotalUzs);
        }

        using var check = NewDb();
        var bar = BarFor(check);
        Assert.Equal(7, (await check.BarProducts.SingleAsync()).Stock);

        // A later price change must not rewrite the old sale.
        await bar.UpdateProductAsync(id, "Cola", 9_000);
        var sale2 = await check.BarSales.AsNoTracking().SingleAsync();
        Assert.Equal(5_000, sale2.UnitPriceUzs);
        Assert.Equal(15_000, sale2.TotalUzs);

        var log = await bar.GetStockLogAsync();
        Assert.Equal(new[] { BarStockMovementType.Sale, BarStockMovementType.Initial }, log.Select(l => l.Type));
        Assert.Equal(new[] { -3, 10 }, log.Select(l => l.Delta));
        Assert.Equal(new[] { 7, 10 }, log.Select(l => l.StockAfter));
        Assert.Contains(await check.EventLogEntries.ToListAsync(), e => e.Type == EventType.BarSale && e.Message.Contains("Cola"));
    }

    [Fact]
    public async Task Overselling_and_archived_products_are_rejected_and_change_nothing()
    {
        var id = await NewProductAsync(stock: 2);

        using (var db = NewDb())
            await Assert.ThrowsAsync<InvalidOperationException>(() => BarFor(db).SellAsync(new BarService.SaleRequest(id, 3)));

        using (var db = NewDb())
            await BarFor(db).SetArchivedAsync(id, true);
        using (var db = NewDb())
            await Assert.ThrowsAsync<InvalidOperationException>(() => BarFor(db).SellAsync(new BarService.SaleRequest(id, 1)));

        using var check = NewDb();
        Assert.Equal(2, (await check.BarProducts.SingleAsync()).Stock);
        Assert.Empty(check.BarSales);
        Assert.Single(check.BarStockMovements); // only the initial stock
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, BarService.MaxCups + 1)]
    [InlineData(BarService.MaxQuantity + 1, 0)]
    public async Task Quantity_and_cups_must_be_in_range(int quantity, int cups)
    {
        var id = await NewProductAsync(stock: 1_000);
        using var db = NewDb();
        await Assert.ThrowsAsync<InvalidOperationException>(() => BarFor(db).SellAsync(new BarService.SaleRequest(id, quantity, cups)));
    }

    [Fact]
    public async Task Cups_are_charged_at_the_cup_price_and_the_price_is_snapshotted()
    {
        var id = await NewProductAsync();
        using (var db = NewDb())
            await BarFor(db).SetCupPriceAsync(500);

        using (var db = NewDb())
        {
            var sale = await BarFor(db).SellAsync(new BarService.SaleRequest(id, 2, Cups: 3));
            Assert.Equal(2 * 5_000 + 3 * 500, sale.TotalUzs);
        }

        using (var db = NewDb())
            await BarFor(db).SetCupPriceAsync(1_000);

        using var check = NewDb();
        var stored = await check.BarSales.SingleAsync();
        Assert.Equal(500, stored.CupPriceUzs);
        Assert.Equal(11_500, stored.TotalUzs);
        Assert.Equal(1_000, await BarFor(check).GetCupPriceAsync());
    }

    [Fact]
    public async Task Cancelling_a_sale_puts_the_stock_back_and_cannot_repeat()
    {
        var id = await NewProductAsync();
        int saleId;
        using (var db = NewDb())
            saleId = (await BarFor(db).SellAsync(new BarService.SaleRequest(id, 4))).Id;

        using (var db = NewDb())
            await BarFor(db).VoidSaleAsync(saleId);

        using (var db = NewDb())
        {
            Assert.Equal(10, (await db.BarProducts.SingleAsync()).Stock);
            Assert.NotNull((await db.BarSales.SingleAsync()).VoidedUtc);
            Assert.Equal(BarStockMovementType.Void, (await BarFor(db).GetStockLogAsync()).First().Type);
            await Assert.ThrowsAsync<InvalidOperationException>(() => BarFor(db).VoidSaleAsync(saleId));
            Assert.Equal(10, (await db.BarProducts.SingleAsync()).Stock);
        }
    }

    [Fact]
    public async Task Popular_first_counts_units_sold_ignores_cancelled_sales_and_falls_back_to_name()
    {
        var apple = await NewProductAsync("Apple", stock: 50);
        var bread = await NewProductAsync("bread", stock: 50);
        var cola = await NewProductAsync("Cola", stock: 50);
        await NewProductAsync("Dates", stock: 50); // never sold

        using (var db = NewDb())
        {
            var bar = BarFor(db);
            await bar.SellAsync(new BarService.SaleRequest(bread, 2));
            await bar.SellAsync(new BarService.SaleRequest(cola, 5));
            var cancelled = await bar.SellAsync(new BarService.SaleRequest(apple, 9));
            await bar.VoidSaleAsync(cancelled.Id);
        }

        using var check = NewDb();
        var popular = await BarFor(check).ListProductsAsync(popularFirst: true);
        Assert.Equal(new[] { "Cola", "bread", "Apple", "Dates" }, popular.Select(p => p.Name));

        var plain = await BarFor(check).ListProductsAsync();
        Assert.Equal(new[] { "Apple", "bread", "Cola", "Dates" }, plain.Select(p => p.Name));

        var found = await BarFor(check).ListProductsAsync("OL", popularFirst: true);
        Assert.Equal(new[] { "Cola" }, found.Select(p => p.Name));
    }

    [Fact]
    public async Task Stock_adjustments_need_a_reason_stay_in_range_and_are_logged()
    {
        var id = await NewProductAsync(stock: 5);
        using var db = NewDb();
        var bar = BarFor(db);

        Assert.Equal(15, await bar.AdjustStockAsync(id, 10, "restock", " delivery "));
        Assert.Equal(12, await bar.AdjustStockAsync(id, -3, "damaged"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => bar.AdjustStockAsync(id, 1, "because"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bar.AdjustStockAsync(id, 0, "recount"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bar.AdjustStockAsync(id, -13, "recount"));
        Assert.Equal(12, (await db.BarProducts.SingleAsync()).Stock);

        var log = await bar.GetStockLogAsync(id);
        Assert.Equal(new[] { 12, 15, 5 }, log.Select(l => l.StockAfter));
        Assert.Equal("delivery", log[1].Note);
        Assert.Equal("restock", log[1].Reason);
    }

    // ---- unpaid ----

    [Fact]
    public async Task An_unpaid_sale_needs_a_comment_shows_in_the_tracker_and_mark_paid_clears_it()
    {
        var id = await NewProductAsync();
        using var db = NewDb();
        var bar = BarFor(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => bar.SellAsync(new BarService.SaleRequest(id, 1, Unpaid: true, Comment: "  ")));
        Assert.Equal(10, (await db.BarProducts.SingleAsync()).Stock);

        var one = await bar.SellAsync(new BarService.SaleRequest(id, 1, Unpaid: true, Comment: " Aziz, PC 4 "));
        await bar.SellAsync(new BarService.SaleRequest(id, 2)); // a normal sale is not unpaid
        await bar.SellAsync(new BarService.SaleRequest(id, 2, Unpaid: true, Comment: "Bobur"));

        var summary = await bar.GetUnpaidSummaryAsync();
        Assert.Equal(2, summary.Count);
        Assert.Equal(15_000, summary.TotalUzs);
        Assert.Equal("Aziz, PC 4", (await bar.GetUnpaidAsync()).First().UnpaidComment);

        await bar.MarkPaidAsync(one.Id);
        Assert.Equal(new BarService.UnpaidSummary(1, 10_000), await bar.GetUnpaidSummaryAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => bar.MarkPaidAsync(one.Id)); // already paid
        Assert.Contains(await db.EventLogEntries.ToListAsync(), e => e.Type == EventType.BarPaid);
    }

    [Fact]
    public async Task A_cancelled_unpaid_sale_leaves_the_tracker()
    {
        var id = await NewProductAsync();
        using var db = NewDb();
        var bar = BarFor(db);
        var sale = await bar.SellAsync(new BarService.SaleRequest(id, 1, Unpaid: true, Comment: "Aziz"));

        await bar.VoidSaleAsync(sale.Id);

        Assert.Equal(new BarService.UnpaidSummary(0, 0), await bar.GetUnpaidSummaryAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => bar.MarkPaidAsync(sale.Id));
    }

    // ---- selling to a PC ----

    [Fact]
    public async Task A_paid_sale_to_a_session_is_a_bar_sale_but_not_on_the_bill()
    {
        var id = await NewProductAsync();
        using var db = NewDb();
        await ServiceFor(db).StartPostpayAsync(Pc1);

        await BarFor(db).SellAsync(new BarService.SaleRequest(id, 2, Cups: 1, SessionMachineGuid: Pc1));

        using var check = NewDb();
        Assert.Equal(8, (await check.BarProducts.SingleAsync()).Stock);          // stock down: it is a bar sale
        var stored = await check.BarSales.SingleAsync();
        Assert.Equal("PC 1", stored.PcLabel);
        Assert.Null(stored.SessionExtraId);                                       // the client already paid: not on the bill
        Assert.False(stored.IsUnpaid);

        Assert.Empty(await ServiceFor(check).GetExtrasAsync(Pc1));
        Assert.Equal(0, (await ServiceFor(check).GetActiveSessionSnapshotAsync(Pc1))!.ExtrasUzs);
        Assert.Equal(new BarService.UnpaidSummary(0, 0), await BarFor(check).GetUnpaidSummaryAsync());
    }

    [Fact]
    public async Task An_unpaid_sale_to_a_session_is_a_bar_sale_and_a_red_item_on_the_bill()
    {
        var id = await NewProductAsync();
        using var db = NewDb();
        await ServiceFor(db).StartPostpayAsync(Pc1);

        var sale = await BarFor(db).SellAsync(new BarService.SaleRequest(id, 1, Cups: 1, SessionMachineGuid: Pc1, Unpaid: true, Comment: "pays at the end"));

        using var check = NewDb();
        Assert.Equal(9, (await check.BarProducts.SingleAsync()).Stock);
        var stored = await check.BarSales.SingleAsync();
        Assert.Equal("PC 1", stored.PcLabel);
        Assert.NotNull(stored.SessionExtraId);
        Assert.True(stored.IsUnpaid);

        var item = (await ServiceFor(check).GetExtrasAsync(Pc1)).Single();       // on the bill, flagged so it shows in red
        Assert.Equal(sale.TotalUzs, item.AmountUzs);
        Assert.Contains("Cola", item.Description);
        Assert.True(item.IsUnpaid);
        Assert.Equal(sale.TotalUzs, (await ServiceFor(check).GetActiveSessionSnapshotAsync(Pc1))!.ExtrasUzs);
        Assert.Equal(1, (await BarFor(check).GetUnpaidSummaryAsync()).Count);     // still owed until the session is stopped
    }

    [Fact]
    public async Task Other_items_are_not_red()
    {
        using var db = NewDb();
        await ServiceFor(db).StartPostpayAsync(Pc1);
        await ServiceFor(db).AddExtraAsync(Pc1, "Charger", 3_000);

        Assert.False((await ServiceFor(db).GetExtrasAsync(Pc1)).Single().IsUnpaid);
    }

    [Fact]
    public async Task Stopping_the_session_settles_its_unpaid_sales_and_leaves_paid_ones_alone()
    {
        var id = await NewProductAsync();
        using (var db = NewDb())
        {
            await ServiceFor(db).StartPostpayAsync(Pc1);
            await BarFor(db).SellAsync(new BarService.SaleRequest(id, 1, SessionMachineGuid: Pc1));                                  // paid at once
            await BarFor(db).SellAsync(new BarService.SaleRequest(id, 2, SessionMachineGuid: Pc1, Unpaid: true, Comment: "later")); // on the bill
            await BarFor(db).SellAsync(new BarService.SaleRequest(id, 1, Unpaid: true, Comment: "Ali owes"));                       // counter debt, unrelated
        }

        using (var db = NewDb())
            await ServiceFor(db).EndSessionEarlyAsync(Pc1);

        using var check = NewDb();
        var sales = await check.BarSales.OrderBy(s => s.Id).ToListAsync();
        Assert.Null(sales[0].PaidUtc);                   // paid sale: untouched
        Assert.NotNull(sales[1].PaidUtc);                // its bill was paid
        Assert.NotNull(sales[1].PaidShiftId);
        Assert.Null(sales[2].PaidUtc);                   // the counter debt is still owed
        Assert.Equal(new BarService.UnpaidSummary(1, 5_000), await BarFor(check).GetUnpaidSummaryAsync());
    }

    [Fact]
    public async Task Marking_an_unpaid_sale_paid_while_the_session_runs_takes_it_off_the_bill()
    {
        var id = await NewProductAsync();
        int saleId;
        using (var db = NewDb())
        {
            await ServiceFor(db).StartPostpayAsync(Pc1);
            saleId = (await BarFor(db).SellAsync(new BarService.SaleRequest(id, 1, SessionMachineGuid: Pc1, Unpaid: true, Comment: "paid at the counter"))).Id;
        }

        using (var db = NewDb())
            await BarFor(db).MarkPaidAsync(saleId);

        using var check = NewDb();
        Assert.Empty(await ServiceFor(check).GetExtrasAsync(Pc1));                // or the client would pay twice
        Assert.Equal(0, (await ServiceFor(check).GetActiveSessionSnapshotAsync(Pc1))!.ExtrasUzs);
        var stored = await check.BarSales.SingleAsync();
        Assert.NotNull(stored.PaidUtc);
        Assert.Null(stored.SessionExtraId);
    }

    [Fact]
    public async Task Selling_to_a_PC_without_a_running_session_fails_and_changes_nothing()
    {
        var id = await NewProductAsync();
        using var db = NewDb();

        await Assert.ThrowsAsync<InvalidOperationException>(() => BarFor(db).SellAsync(new BarService.SaleRequest(id, 1, SessionMachineGuid: Pc1)));

        using var check = NewDb();
        Assert.Equal(10, (await check.BarProducts.SingleAsync()).Stock);
        Assert.Empty(check.BarSales);
    }

    [Fact]
    public async Task Cancelling_a_sale_on_a_running_session_removes_its_item_and_after_the_session_it_is_refused()
    {
        var id = await NewProductAsync();
        using (var db = NewDb())
            await ServiceFor(db).StartPostpayAsync(Pc1);

        int first, second;
        using (var db = NewDb())
        {
            first = (await BarFor(db).SellAsync(new BarService.SaleRequest(id, 1, SessionMachineGuid: Pc1, Unpaid: true, Comment: "later"))).Id;
            second = (await BarFor(db).SellAsync(new BarService.SaleRequest(id, 2, SessionMachineGuid: Pc1, Unpaid: true, Comment: "later"))).Id;
        }

        using (var db = NewDb())
        {
            await BarFor(db).VoidSaleAsync(first);
            Assert.Equal(8, (await db.BarProducts.SingleAsync()).Stock);
            Assert.Equal(10_000, (await ServiceFor(db).GetExtrasAsync(Pc1)).Sum(i => i.AmountUzs)); // only the second item left
        }

        using (var db = NewDb())
            await ServiceFor(db).EndSessionEarlyAsync(Pc1);

        using (var db = NewDb())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => BarFor(db).VoidSaleAsync(second));
            Assert.Equal(8, (await db.BarProducts.SingleAsync()).Stock);
            Assert.Null((await db.BarSales.SingleAsync(s => s.Id == second)).VoidedUtc);
        }
    }

    [Fact]
    public async Task Removing_the_item_from_the_session_also_cancels_the_bar_sale()
    {
        var id = await NewProductAsync();
        using (var db = NewDb())
        {
            await ServiceFor(db).StartPostpayAsync(Pc1);
            await BarFor(db).SellAsync(new BarService.SaleRequest(id, 3, SessionMachineGuid: Pc1, Unpaid: true, Comment: "later"));
        }

        using (var db = NewDb())
        {
            var item = (await ServiceFor(db).GetExtrasAsync(Pc1)).Single();
            await ServiceFor(db).RemoveExtraAsync(item.Id);
        }

        using var check = NewDb();
        Assert.Equal(10, (await check.BarProducts.SingleAsync()).Stock);
        Assert.NotNull((await check.BarSales.SingleAsync()).VoidedUtc);
        Assert.Empty(await ServiceFor(check).GetExtrasAsync(Pc1));
    }

    [Fact]
    public async Task Product_names_and_prices_are_validated()
    {
        using var db = NewDb();
        var bar = BarFor(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bar.CreateProductAsync("  ", 1_000, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bar.CreateProductAsync(new string('x', 101), 1_000, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bar.CreateProductAsync("Cola", -1, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bar.CreateProductAsync("Cola", 1_000, -1));
        Assert.Empty(db.BarProducts);
    }
}
