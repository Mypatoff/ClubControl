using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Services;

/// <summary>
/// One-time import of the old ShopKeeper program's database (shop.db) into the bar: products, sales
/// (cups and cancelled ones included), the stock log and the cup price. The old file is only read.
/// Everything is copied in one transaction after a safety copy of club.db, so a failed import changes nothing.
/// </summary>
public class BarImportService
{
    /// <summary>The ShopKeeper schema version this reads (v1.3.x). Older files must be opened once with ShopKeeper to upgrade them.</summary>
    public const int SupportedShopVersion = 5;

    public record Result(int Products, int Sales, int Movements);

    private readonly ClubDbContext _db;

    public BarImportService(ClubDbContext db)
    {
        _db = db;
    }

    // What was read from the old file, before anything is written.
    private sealed record OldProduct(long Id, string Name, long Price, long Stock, bool Archived, long CreatedAt);
    private sealed record OldSale(long Id, long ProductId, string ProductName, long Quantity, long UnitPrice, long Cups, long CupPrice, long Total, long CreatedAt, long? VoidedAt);
    private sealed record OldMovement(long ProductId, string Type, long Delta, long StockAfter, string Reason, string Note, long? SaleId, long CreatedAt);
    private sealed record OldData(List<OldProduct> Products, List<OldSale> Sales, List<OldMovement> Movements, long CupPrice);

    /// <param name="backupDirectory">Where the safety copy of club.db goes; the normal backup folder when null.</param>
    public async Task<Result> ImportAsync(string shopDbPath, string? backupDirectory = null)
    {
        var old = Read(shopDbPath);

        if (await _db.BarProducts.AnyAsync() || await _db.BarSales.AnyAsync())
            throw new InvalidOperationException("The bar already has products or sales. The import is only for an empty bar, so nothing is imported twice.");

        // A full copy first: this is the one step that cannot be undone from inside the program.
        await _db.Database.OpenConnectionAsync();
        var backupPath = Path.Combine(backupDirectory ?? DbPaths.BackupDirectory, $"club_before-bar-import_{DateTime.Now:yyyy-MM-dd_HHmmss}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        DatabaseBackup.CopyTo((SqliteConnection)_db.Database.GetDbConnection(), backupPath);

        await using var transaction = await _db.Database.BeginTransactionAsync();

        var products = new Dictionary<long, BarProduct>();
        foreach (var p in old.Products)
        {
            var product = new BarProduct
            {
                Name = Clean(p.Name, BarService.MaxNameLength),
                PriceUzs = ToUzs(p.Price, $"the price of \"{p.Name}\""),
                Stock = ToStock(p.Stock, p.Name),
                Archived = p.Archived,
                CreatedUtc = FromUnix(p.CreatedAt)
            };
            products[p.Id] = product;
            _db.BarProducts.Add(product);
        }

        var sales = new Dictionary<long, BarSale>();
        foreach (var s in old.Sales)
        {
            var sale = new BarSale
            {
                BarProduct = products.GetValueOrDefault(s.ProductId)
                    ?? throw new InvalidOperationException($"Sale {s.Id} refers to a product that is not in the file."),
                ProductName = Clean(s.ProductName, BarService.MaxNameLength),
                UnitPriceUzs = ToUzs(s.UnitPrice, $"the price in sale {s.Id}"),
                Quantity = ToCount(s.Quantity, $"the quantity in sale {s.Id}"),
                Cups = ToCount(s.Cups, $"the cups in sale {s.Id}"),
                CupPriceUzs = ToUzs(s.CupPrice, $"the cup price in sale {s.Id}"),
                TotalUzs = ToUzs(s.Total, $"the total of sale {s.Id}"),
                CreatedUtc = FromUnix(s.CreatedAt),
                VoidedUtc = s.VoidedAt is { } v ? FromUnix(v) : null
            };
            sales[s.Id] = sale;
            _db.BarSales.Add(sale);
        }

        foreach (var m in old.Movements)
        {
            _db.BarStockMovements.Add(new BarStockMovement
            {
                BarProduct = products.GetValueOrDefault(m.ProductId)
                    ?? throw new InvalidOperationException("A stock record refers to a product that is not in the file."),
                Type = m.Type switch
                {
                    "initial" => BarStockMovementType.Initial,
                    "sale" => BarStockMovementType.Sale,
                    "void" => BarStockMovementType.Void,
                    "adjust" => BarStockMovementType.Adjust,
                    _ => throw new InvalidOperationException($"Unknown stock record type \"{m.Type}\".")
                },
                Delta = ToInt(m.Delta, "a stock change"),
                StockAfter = ToInt(m.StockAfter, "a stock level"),
                Reason = m.Reason,
                Note = m.Note,
                BarSale = m.SaleId is { } id ? sales.GetValueOrDefault(id) : null,
                CreatedUtc = FromUnix(m.CreatedAt)
            });
        }

        if (old.CupPrice > 0)
            (await SettingsHelper.GetOrCreateAsync(_db)).BarCupPriceUzs = ToUzs(old.CupPrice, "the cup price");

        _db.EventLogEntries.Add(new EventLogEntry
        {
            Type = EventType.BarSale,
            Message = $"Bar import: {old.Products.Count} products, {old.Sales.Count} sales, {old.Movements.Count} stock records"
        });

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new Result(old.Products.Count, old.Sales.Count, old.Movements.Count);
    }

    // ---- reading the old file ----

    private static OldData Read(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException("That file does not exist.");

        try
        {
            // Read-only, and no pooling so the file is not left locked once the import is done.
            using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            conn.Open();

            if (Scalar(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('products', 'sales', 'stock_movements', 'settings')") != 4)
                throw new InvalidOperationException("This does not look like a ShopKeeper database (products, sales or stock tables are missing).");

            var version = Scalar(conn, "PRAGMA user_version");
            if (version != SupportedShopVersion)
                throw new InvalidOperationException(version < SupportedShopVersion
                    ? $"This database is from an older ShopKeeper (version {version}). Open it once with ShopKeeper 1.3 or newer, close it, then import it."
                    : $"This database is from a newer ShopKeeper (version {version}) than this ClubControl can read.");

            var products = new List<OldProduct>();
            using (var r = Query(conn, "SELECT id, name, price, stock, archived, created_at FROM products ORDER BY id"))
                while (r.Read())
                    products.Add(new OldProduct(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4) != 0, r.GetInt64(5)));

            var sales = new List<OldSale>();
            using (var r = Query(conn, "SELECT id, product_id, product_name, quantity, unit_price, cups, cup_price, total, created_at, voided_at FROM sales ORDER BY id"))
                while (r.Read())
                    sales.Add(new OldSale(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4), r.GetInt64(5), r.GetInt64(6),
                        r.GetInt64(7), r.GetInt64(8), r.IsDBNull(9) ? null : r.GetInt64(9)));

            var movements = new List<OldMovement>();
            using (var r = Query(conn, "SELECT product_id, type, delta, stock_after, reason, note, sale_id, created_at FROM stock_movements ORDER BY id"))
                while (r.Read())
                    movements.Add(new OldMovement(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3),
                        r.IsDBNull(4) ? "" : r.GetString(4), r.IsDBNull(5) ? "" : r.GetString(5), r.IsDBNull(6) ? null : r.GetInt64(6), r.GetInt64(7)));

            long cupPrice = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT value FROM settings WHERE key = 'cup_price'";
                if (cmd.ExecuteScalar() is string text && long.TryParse(text, out var parsed))
                    cupPrice = parsed;
            }

            return new OldData(products, sales, movements, cupPrice);
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException($"That file could not be read as a ShopKeeper database ({ex.Message}).", ex);
        }
    }

    private static long Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static SqliteDataReader Query(SqliteConnection conn, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteReader();
    }

    // ---- converting values ----

    private static DateTime FromUnix(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

    private static string Clean(string text, int max)
    {
        text = text.Trim();
        return text.Length > max ? text[..max] : text;
    }

    private static int ToUzs(long value, string what)
    {
        if (value < 0 || value > BarService.MaxPriceUzs)
            throw new InvalidOperationException($"{what} ({value}) is outside what the bar can hold (0 to {BarService.MaxPriceUzs} UZS).");
        return (int)value;
    }

    private static int ToStock(long value, string product)
    {
        if (value < 0 || value > BarService.MaxStock)
            throw new InvalidOperationException($"The stock of \"{product}\" ({value}) is outside 0 to {BarService.MaxStock}.");
        return (int)value;
    }

    private static int ToInt(long value, string what)
    {
        if (value < int.MinValue || value > int.MaxValue)
            throw new InvalidOperationException($"{what} ({value}) is not valid.");
        return (int)value;
    }

    private static int ToCount(long value, string what)
    {
        if (value < 0 || value > int.MaxValue)
            throw new InvalidOperationException($"{what} ({value}) is not valid.");
        return (int)value;
    }
}
