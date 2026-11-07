using ClubControl.Server.Data;
using ClubControl.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>Importing the old ShopKeeper database (shop.db) into the bar.</summary>
public class BarImportTests : SessionTestBase, IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clubcontrol-import-" + Guid.NewGuid().ToString("N"));

    public BarImportTests() => Directory.CreateDirectory(_folder);

    public new void Dispose()
    {
        base.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { /* a temp folder: left behind at worst */ }
    }

    private string BackupFolder => Path.Combine(_folder, "backups");

    /// <summary>A ShopKeeper v1.3 database with 3 products (one archived, one with a cup price in use), 3 sales and 5 stock records.</summary>
    private string MakeShopDb(int userVersion = 5, bool saleWithMissingProduct = false)
    {
        var path = Path.Combine(_folder, $"shop-{Guid.NewGuid():N}.db");
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();

        void Run(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        Run("""
            CREATE TABLE products (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, sku TEXT UNIQUE, price INTEGER NOT NULL,
                stock INTEGER NOT NULL DEFAULT 0, low_stock_threshold INTEGER NOT NULL DEFAULT 5, archived INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL);
            CREATE TABLE sales (id INTEGER PRIMARY KEY AUTOINCREMENT, product_id INTEGER NOT NULL, product_name TEXT NOT NULL,
                quantity INTEGER NOT NULL, unit_price INTEGER NOT NULL, total INTEGER NOT NULL, created_at INTEGER NOT NULL, voided_at INTEGER,
                cups INTEGER NOT NULL DEFAULT 0, cup_price INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE stock_movements (id INTEGER PRIMARY KEY AUTOINCREMENT, product_id INTEGER NOT NULL, type TEXT NOT NULL, delta INTEGER NOT NULL,
                stock_after INTEGER NOT NULL, reason TEXT, note TEXT, sale_id INTEGER, created_at INTEGER NOT NULL);
            CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """);
        Run($"PRAGMA user_version = {userVersion}");

        Run("""
            INSERT INTO products (id, name, sku, price, stock, archived, created_at, updated_at) VALUES
                (1, 'Cola 1.5', 'C15', 16000, 9, 0, 1760000000, 1760000000),
                (2, 'Tara', NULL, 5000, 40, 0, 1760000100, 1760000100),
                (3, 'Old thing', NULL, 1000, 0, 1, 1760000200, 1760000200);
            INSERT INTO settings (key, value) VALUES ('cup_price', '500');
            """);
        Run($"""
            INSERT INTO sales (id, product_id, product_name, quantity, unit_price, total, created_at, voided_at, cups, cup_price) VALUES
                (1, 1, 'Cola 1.5', 1, 16000, 16500, 1760001000, NULL, 1, 500),
                (2, 2, 'Tara', 2, 5000, 10000, 1760002000, 1760002500, 0, 0),
                (3, {(saleWithMissingProduct ? 99 : 2)}, 'Tara', 1, 5000, 5000, 1760003000, NULL, 0, 0);
            INSERT INTO stock_movements (product_id, type, delta, stock_after, reason, note, sale_id, created_at) VALUES
                (1, 'initial', 10, 10, NULL, NULL, NULL, 1760000000),
                (1, 'sale', -1, 9, NULL, NULL, 1, 1760001000),
                (2, 'initial', 43, 43, NULL, NULL, NULL, 1760000100),
                (2, 'sale', -2, 41, NULL, NULL, 2, 1760002000),
                (2, 'void', 2, 43, NULL, NULL, 2, 1760002500);
            """);
        return path;
    }

    [Fact]
    public async Task Everything_is_copied_with_its_prices_cups_voids_and_links()
    {
        var shop = MakeShopDb();

        using (var db = NewDb())
        {
            var result = await new BarImportService(db).ImportAsync(shop, BackupFolder);
            Assert.Equal(new BarImportService.Result(3, 3, 5), result);
        }

        using var check = NewDb();
        var products = await check.BarProducts.OrderBy(p => p.Id).ToListAsync();
        Assert.Equal(new[] { "Cola 1.5", "Tara", "Old thing" }, products.Select(p => p.Name));
        Assert.Equal(new[] { 16_000, 5_000, 1_000 }, products.Select(p => p.PriceUzs));
        Assert.Equal(new[] { 9, 40, 0 }, products.Select(p => p.Stock));
        Assert.Equal(new[] { false, false, true }, products.Select(p => p.Archived));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_760_000_000).UtcDateTime, products[0].CreatedUtc);

        var sales = await check.BarSales.Include(s => s.BarProduct).OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(new[] { 16_500, 10_000, 5_000 }, sales.Select(s => s.TotalUzs));
        Assert.Equal(1, sales[0].Cups);
        Assert.Equal(500, sales[0].CupPriceUzs);
        Assert.Equal("Cola 1.5", sales[0].BarProduct!.Name);
        Assert.Null(sales[0].VoidedUtc);
        Assert.NotNull(sales[1].VoidedUtc);
        Assert.All(sales, s => Assert.False(s.IsUnpaid));
        Assert.All(sales, s => Assert.Null(s.SessionId));

        var bar = new BarService(check, ServiceFor(check));
        Assert.Equal(500, await bar.GetCupPriceAsync());

        var log = await check.BarStockMovements.Include(m => m.BarSale).OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(5, log.Count);
        Assert.Equal(sales[1].Id, log[4].BarSaleId); // the void points at the (new id of the) cancelled sale
        Assert.Equal(Data.Models.BarStockMovementType.Void, log[4].Type);

        // The imported bar behaves like any other: the stock it carries can be sold and the sale shows up.
        await bar.SellAsync(new BarService.SaleRequest(products[1].Id, 1));
        Assert.Equal(39, (await check.BarProducts.SingleAsync(p => p.Name == "Tara")).Stock);
        Assert.True(Directory.GetFiles(BackupFolder, "club_before-bar-import_*.db").Length == 1);
    }

    [Fact]
    public async Task A_second_import_is_refused_and_adds_nothing()
    {
        var shop = MakeShopDb();
        using (var db = NewDb())
            await new BarImportService(db).ImportAsync(shop, BackupFolder);

        using (var db = NewDb())
            await Assert.ThrowsAsync<InvalidOperationException>(() => new BarImportService(db).ImportAsync(shop, BackupFolder));

        using var check = NewDb();
        Assert.Equal(3, await check.BarProducts.CountAsync());
        Assert.Equal(3, await check.BarSales.CountAsync());
    }

    [Fact]
    public async Task Files_that_are_not_a_current_shopkeeper_database_are_refused()
    {
        var notADatabase = Path.Combine(_folder, "notes.db");
        File.WriteAllText(notADatabase, "this is not a sqlite file at all, just some text to make it long enough");

        using var db = NewDb();
        var import = new BarImportService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => import.ImportAsync(notADatabase, BackupFolder));
        await Assert.ThrowsAsync<InvalidOperationException>(() => import.ImportAsync(Path.Combine(_folder, "missing.db"), BackupFolder));
        await Assert.ThrowsAsync<InvalidOperationException>(() => import.ImportAsync(MakeShopDb(userVersion: 4), BackupFolder));
        await Assert.ThrowsAsync<InvalidOperationException>(() => import.ImportAsync(MakeShopDb(userVersion: 6), BackupFolder));

        using var check = NewDb();
        Assert.Empty(check.BarProducts);
    }

    [Fact]
    public async Task A_broken_file_imports_nothing_at_all()
    {
        var shop = MakeShopDb(saleWithMissingProduct: true);

        using (var db = NewDb())
            await Assert.ThrowsAsync<InvalidOperationException>(() => new BarImportService(db).ImportAsync(shop, BackupFolder));

        using var check = NewDb();
        Assert.Empty(check.BarProducts);
        Assert.Empty(check.BarSales);
        Assert.Empty(check.BarStockMovements);
        Assert.Equal(0, await new BarService(check, ServiceFor(check)).GetCupPriceAsync());
    }
}
