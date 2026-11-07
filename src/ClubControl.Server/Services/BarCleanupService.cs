using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClubControl.Server.Services;

/// <summary>
/// Keeps the bar's history to the last month (<see cref="ReportsService.BarHistoryDays"/> days): once a day it
/// deletes sales, and the stock-log lines that belong to them, that are older than that. Sales still owed
/// are never deleted, and neither is a debt paid within the last month. The first run is a few minutes after
/// start, so the day's database backup (which keeps 30 days) has been taken before anything is removed.
/// </summary>
public class BarCleanupService : BackgroundService
{
    private static readonly TimeSpan FirstRunAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RunEvery = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BarCleanupService> _logger;

    public BarCleanupService(IServiceScopeFactory scopeFactory, ILogger<BarCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(FirstRunAfter, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await PurgeOldAsync(scope.ServiceProvider.GetRequiredService<ClubDbContext>(), DateTime.UtcNow);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Old history staying one more day is harmless; try again at the next run.
                _logger.LogError(ex, "Removing old bar history failed; will retry tomorrow.");
            }

            await Task.Delay(RunEvery, stoppingToken);
        }
    }

    public record Result(int Sales, int StockLines);

    /// <summary>
    /// Deletes bar sales older than the history window together with their stock-log lines, then the other stock-log
    /// lines (first stock, manual adjustments) older than the window. A sale stays while it is still owed, or
    /// was paid or cancelled within the window, because that is when its money counts.
    /// </summary>
    public static async Task<Result> PurgeOldAsync(ClubDbContext db, DateTime nowUtc)
    {
        var cutoff = nowUtc.AddDays(-ReportsService.BarHistoryDays);

        await using var transaction = await db.Database.BeginTransactionAsync();

        var oldSaleIds = await db.BarSales
            .Where(s => s.CreatedUtc < cutoff
                && (s.VoidedUtc == null || s.VoidedUtc < cutoff)
                && (s.PaidUtc == null || s.PaidUtc < cutoff)
                && !(s.IsUnpaid && s.PaidUtc == null && s.VoidedUtc == null))
            .Select(s => s.Id)
            .ToListAsync();

        var sales = 0;
        var stockLines = 0;
        foreach (var chunk in oldSaleIds.Chunk(400)) // SQLite limits how many values one statement can carry
        {
            var ids = chunk.ToList();
            stockLines += await db.BarStockMovements.Where(m => m.BarSaleId != null && ids.Contains(m.BarSaleId.Value)).ExecuteDeleteAsync();
            sales += await db.BarSales.Where(s => ids.Contains(s.Id)).ExecuteDeleteAsync();
        }

        stockLines += await db.BarStockMovements.Where(m => m.BarSaleId == null && m.CreatedUtc < cutoff).ExecuteDeleteAsync();

        if (sales + stockLines > 0)
        {
            db.EventLogEntries.Add(new EventLogEntry
            {
                Type = EventType.BarSale,
                Message = $"Bar history older than {ReportsService.BarHistoryDays} days removed: {sales} sales, {stockLines} stock log lines"
            });
            await db.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        return new Result(sales, stockLines);
    }
}
