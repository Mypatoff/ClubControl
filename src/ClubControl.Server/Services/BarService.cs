using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Services;

/// <summary>
/// The bar: products with stock, sales (at the counter or added to a PC's running session, paid or
/// on credit), cancelling a sale, and the stock log. A port of the useful parts of the old
/// ShopKeeper program. Every change that touches stock runs in one transaction together with its
/// stock-log row and event-log entry, so the numbers can always be explained.
/// </summary>
public class BarService
{
    public const int MaxPriceUzs = SessionService.MaxExtraUzs;
    public const int MaxStock = 1_000_000;
    public const int MaxQuantity = 100_000;
    public const int MaxCups = 1_000;
    public const int MaxNameLength = 100;
    public const int MaxCommentLength = 200;

    /// <summary>The reasons a manual stock change can have, stored as this lower-case text.</summary>
    public static readonly string[] AdjustReasons = { "restock", "recount", "damaged", "other" };

    /// <summary>One sale to make. A null <paramref name="SessionMachineGuid"/> means a counter sale.</summary>
    public record SaleRequest(int ProductId, int Quantity, int Cups = 0, Guid? SessionMachineGuid = null, bool Unpaid = false, string? Comment = null);

    /// <summary>What the unpaid chip shows: how many sales are waiting for payment and how much they add up to.</summary>
    public record UnpaidSummary(int Count, long TotalUzs);

    /// <summary>One row of the stock log.</summary>
    public record StockLogLine(int Id, DateTime CreatedUtc, string ProductName, BarStockMovementType Type, int Delta, int StockAfter, string Reason, string Note, int? BarSaleId);

    private readonly ClubDbContext _db;
    private readonly SessionService _sessions;

    public BarService(ClubDbContext db, SessionService sessions)
    {
        _db = db;
        _sessions = sessions;
    }

    // ---- products ----

    /// <summary>
    /// Products matching <paramref name="search"/> (any part of the name, ignoring case). With
    /// <paramref name="popularFirst"/> the best sellers come first (units sold, cancelled sales not
    /// counted); products with the same count, and ones never sold, are in A-Z order.
    /// </summary>
    public async Task<List<BarProduct>> ListProductsAsync(string? search = null, bool includeArchived = false, bool popularFirst = false)
    {
        var query = _db.BarProducts.AsNoTracking();
        if (!includeArchived)
            query = query.Where(p => !p.Archived);

        var products = await query.ToListAsync();

        // Filtering and sorting happen here, not in SQL: SQLite's LIKE and ORDER BY only know ASCII
        // case rules, and the names are Uzbek and Russian.
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
            products = products.Where(p => p.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var sold = new Dictionary<int, int>();
        if (popularFirst)
        {
            sold = await _db.BarSales
                .Where(s => s.VoidedUtc == null)
                .GroupBy(s => s.BarProductId)
                .Select(g => new { Id = g.Key, Units = g.Sum(s => s.Quantity) })
                .ToDictionaryAsync(x => x.Id, x => x.Units);
        }

        return products
            .OrderByDescending(p => sold.GetValueOrDefault(p.Id))
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<BarProduct> CreateProductAsync(string name, int priceUzs, int stock)
    {
        name = CleanName(name);
        CheckPrice(priceUzs);
        if (stock < 0 || stock > MaxStock)
            throw new InvalidOperationException($"Stock must be between 0 and {MaxStock}.");

        var product = new BarProduct { Name = name, PriceUzs = priceUzs, Stock = stock };
        _db.BarProducts.Add(product);
        if (stock > 0)
            _db.BarStockMovements.Add(new BarStockMovement { BarProduct = product, Type = BarStockMovementType.Initial, Delta = stock, StockAfter = stock });

        await _db.SaveChangesAsync();
        return product;
    }

    /// <summary>Changes the name and price. Sales already made keep the price they were made at.</summary>
    public async Task UpdateProductAsync(int productId, string name, int priceUzs)
    {
        name = CleanName(name);
        CheckPrice(priceUzs);

        var product = await GetProductAsync(productId);
        product.Name = name;
        product.PriceUzs = priceUzs;
        await _db.SaveChangesAsync();
    }

    public async Task SetArchivedAsync(int productId, bool archived)
    {
        var product = await GetProductAsync(productId);
        product.Archived = archived;
        await _db.SaveChangesAsync();
    }

    /// <summary>Adds (positive) or removes (negative) stock by hand, with a reason, and logs it. Returns the new stock.</summary>
    public async Task<int> AdjustStockAsync(int productId, int delta, string reason, string? note = null)
    {
        reason = (reason ?? "").Trim().ToLowerInvariant();
        if (!AdjustReasons.Contains(reason))
            throw new InvalidOperationException("Choose a reason: restock, recount, damaged or other.");
        if (delta == 0)
            throw new InvalidOperationException("The change must not be zero.");

        var product = await GetProductAsync(productId);
        var newStock = (long)product.Stock + delta;
        if (newStock < 0)
            throw new InvalidOperationException($"Only {product.Stock} in stock; it cannot go below zero.");
        if (newStock > MaxStock)
            throw new InvalidOperationException($"Stock cannot go above {MaxStock}.");

        product.Stock = (int)newStock;
        _db.BarStockMovements.Add(new BarStockMovement
        {
            BarProductId = product.Id,
            Type = BarStockMovementType.Adjust,
            Delta = delta,
            StockAfter = product.Stock,
            Reason = reason,
            Note = (note ?? "").Trim()
        });
        await _db.SaveChangesAsync();
        return product.Stock;
    }

    // ---- selling ----

    /// <summary>
    /// Sells a product. At the counter it is just a sale. For a PC (<see cref="SaleRequest.SessionMachineGuid"/>)
    /// it must have a running session: paid, it is added to the session's bill as an item; unpaid,
    /// it is NOT in the bill and waits in the unpaid list instead. Unpaid needs a comment. Stock goes
    /// down, and the sale is checked and saved in one transaction.
    /// </summary>
    public async Task<BarSale> SellAsync(SaleRequest request)
    {
        if (request.Quantity < 1 || request.Quantity > MaxQuantity)
            throw new InvalidOperationException($"Quantity must be between 1 and {MaxQuantity}.");
        if (request.Cups < 0 || request.Cups > MaxCups)
            throw new InvalidOperationException($"Cups must be between 0 and {MaxCups}.");

        var comment = (request.Comment ?? "").Trim();
        if (request.Unpaid && comment == "")
            throw new InvalidOperationException("Write a comment for an unpaid sale (who has to pay it).");
        if (comment.Length > MaxCommentLength)
            throw new InvalidOperationException($"The comment can be at most {MaxCommentLength} characters.");

        await using var transaction = await _db.Database.BeginTransactionAsync();

        var product = await GetProductAsync(request.ProductId);
        if (product.Archived)
            throw new InvalidOperationException("That product is archived and cannot be sold.");
        if (request.Quantity > product.Stock)
            throw new InvalidOperationException($"Only {product.Stock} in stock.");

        var cupPrice = request.Cups > 0 ? (await SettingsHelper.GetOrCreateAsync(_db)).BarCupPriceUzs : 0;
        var total = (long)product.PriceUzs * request.Quantity + (long)request.Cups * cupPrice;
        if (total > MaxPriceUzs)
            throw new InvalidOperationException($"One sale can be at most {MaxPriceUzs} UZS.");

        Pc? pc = null;
        Session? session = null;
        if (request.SessionMachineGuid is { } machineGuid)
            (pc, session) = await _sessions.RequireRunningSessionAsync(machineGuid);

        var sale = new BarSale
        {
            BarProduct = product,
            ProductName = product.Name,
            UnitPriceUzs = product.PriceUzs,
            Quantity = request.Quantity,
            Cups = request.Cups,
            CupPriceUzs = cupPrice,
            TotalUzs = (int)total,
            IsUnpaid = request.Unpaid,
            UnpaidComment = request.Unpaid ? comment : "",
            SessionId = session?.Id,
            PcId = pc?.Id,
            PcLabel = pc is null ? "" : LabelOf(pc)
        };

        // Paid at a PC: it goes on that session's bill like any other item.
        if (session is not null && !request.Unpaid)
            sale.SessionExtra = new SessionExtra { SessionId = session.Id, Description = Describe(sale), AmountUzs = sale.TotalUzs };

        product.Stock -= request.Quantity;
        _db.BarSales.Add(sale);
        _db.BarStockMovements.Add(new BarStockMovement
        {
            BarProduct = product,
            Type = BarStockMovementType.Sale,
            Delta = -request.Quantity,
            StockAfter = product.Stock,
            BarSale = sale
        });

        var where = pc is null ? "" : $" at {sale.PcLabel}";
        var how = request.Unpaid ? $", UNPAID ({comment})" : "";
        _db.EventLogEntries.Add(new EventLogEntry
        {
            Type = EventType.BarSale,
            PcId = pc?.Id,
            SessionId = session?.Id,
            Message = $"Bar sale{where}: {Describe(sale)}, {sale.TotalUzs} UZS{how}"
        });

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return sale;
    }

    /// <summary>
    /// Cancels a sale and puts its stock back. A sale on a PC's bill can only be cancelled while that
    /// session is still running (afterwards it is part of what the client paid).
    /// </summary>
    public async Task VoidSaleAsync(int saleId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();

        var sale = await _db.BarSales.FirstOrDefaultAsync(s => s.Id == saleId)
            ?? throw new InvalidOperationException("That sale no longer exists.");
        if (sale.IsVoided)
            throw new InvalidOperationException("That sale was already cancelled.");

        if (sale.SessionExtraId is { } extraId)
        {
            var extra = await _db.SessionExtras.Include(x => x.Session).FirstOrDefaultAsync(x => x.Id == extraId);
            if (extra is not null)
            {
                if (extra.Session!.Status != SessionStatus.Active)
                    throw new InvalidOperationException("The session has already ended, so this item is part of its bill and cannot be cancelled.");
                _db.SessionExtras.Remove(extra);
            }
        }

        await CancelSaleAsync(_db, sale);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    /// <summary>
    /// Marks the sale cancelled, restores the stock, and adds the stock-log and event-log rows. Does not save:
    /// the caller saves it together with whatever else it changes (<see cref="SessionService.RemoveExtraAsync"/> uses it too).
    /// </summary>
    public static async Task CancelSaleAsync(ClubDbContext db, BarSale sale)
    {
        var product = await db.BarProducts.FirstAsync(p => p.Id == sale.BarProductId);
        product.Stock += sale.Quantity;
        sale.VoidedUtc = DateTime.UtcNow;

        db.BarStockMovements.Add(new BarStockMovement
        {
            BarProductId = product.Id,
            Type = BarStockMovementType.Void,
            Delta = sale.Quantity,
            StockAfter = product.Stock,
            BarSale = sale
        });
        db.EventLogEntries.Add(new EventLogEntry
        {
            Type = EventType.BarSale,
            PcId = sale.PcId,
            SessionId = sale.SessionId,
            Message = $"Bar sale cancelled: {Describe(sale)}, {sale.TotalUzs} UZS"
        });
    }

    // ---- unpaid ----

    /// <summary>Sales on credit that nobody has paid yet, oldest first.</summary>
    public Task<List<BarSale>> GetUnpaidAsync() =>
        _db.BarSales.AsNoTracking()
            .Where(s => s.IsUnpaid && s.PaidUtc == null && s.VoidedUtc == null)
            .OrderBy(s => s.CreatedUtc).ThenBy(s => s.Id)
            .ToListAsync();

    public async Task<UnpaidSummary> GetUnpaidSummaryAsync()
    {
        var rows = _db.BarSales.AsNoTracking().Where(s => s.IsUnpaid && s.PaidUtc == null && s.VoidedUtc == null);
        var count = await rows.CountAsync();
        var total = count == 0 ? 0 : await rows.SumAsync(s => (long)s.TotalUzs);
        return new UnpaidSummary(count, total);
    }

    /// <summary>The client paid a sale that was on credit; from now on it counts as income.</summary>
    public async Task MarkPaidAsync(int saleId)
    {
        var sale = await _db.BarSales.FirstOrDefaultAsync(s => s.Id == saleId)
            ?? throw new InvalidOperationException("That sale no longer exists.");
        if (!sale.IsOutstanding)
            throw new InvalidOperationException("That sale is not waiting for payment.");

        sale.PaidUtc = DateTime.UtcNow;
        _db.EventLogEntries.Add(new EventLogEntry
        {
            Type = EventType.BarPaid,
            PcId = sale.PcId,
            SessionId = sale.SessionId,
            Message = $"Bar sale paid: {Describe(sale)}, {sale.TotalUzs} UZS ({sale.UnpaidComment})"
        });
        await _db.SaveChangesAsync();
    }

    // ---- history ----

    /// <summary>The newest sales first, cancelled ones included (shown struck out).</summary>
    public Task<List<BarSale>> GetRecentSalesAsync(int limit = 200) =>
        _db.BarSales.AsNoTracking().OrderByDescending(s => s.Id).Take(limit).ToListAsync();

    public async Task<List<StockLogLine>> GetStockLogAsync(int? productId = null, int limit = 300)
    {
        var query = _db.BarStockMovements.AsNoTracking().AsQueryable();
        if (productId.HasValue)
            query = query.Where(m => m.BarProductId == productId.Value);

        return await query
            .OrderByDescending(m => m.Id)
            .Take(limit)
            .Select(m => new StockLogLine(m.Id, m.CreatedUtc, m.BarProduct!.Name, m.Type, m.Delta, m.StockAfter, m.Reason, m.Note, m.BarSaleId))
            .ToListAsync();
    }

    // ---- cups ----

    public async Task<int> GetCupPriceAsync() =>
        (await SettingsHelper.GetOrCreateAsync(_db)).BarCupPriceUzs;

    public async Task SetCupPriceAsync(int priceUzs)
    {
        CheckPrice(priceUzs);
        var settings = await SettingsHelper.GetOrCreateAsync(_db);
        settings.BarCupPriceUzs = priceUzs;
        await _db.SaveChangesAsync();
    }

    // ---- helpers ----

    /// <summary>How a sale reads on a session's bill and in the event log: "Cola × 2 + stakan × 1".</summary>
    public static string Describe(BarSale sale) =>
        sale.Cups > 0
            ? $"{sale.ProductName} × {sale.Quantity} + stakan × {sale.Cups}"
            : $"{sale.ProductName} × {sale.Quantity}";

    private static string LabelOf(Pc pc) =>
        pc.Kind == ResourceKind.Other && pc.Name != "" ? pc.Name : $"PC {pc.Number?.ToString() ?? "–"}";

    private async Task<BarProduct> GetProductAsync(int productId) =>
        await _db.BarProducts.FirstOrDefaultAsync(p => p.Id == productId)
            ?? throw new InvalidOperationException("That product no longer exists.");

    private static string CleanName(string? name)
    {
        name = (name ?? "").Trim();
        if (name.Length < 1 || name.Length > MaxNameLength)
            throw new InvalidOperationException($"The name must be 1-{MaxNameLength} characters.");
        return name;
    }

    private static void CheckPrice(int priceUzs)
    {
        if (priceUzs < 0 || priceUzs > MaxPriceUzs)
            throw new InvalidOperationException($"The price must be between 0 and {MaxPriceUzs} UZS.");
    }
}
