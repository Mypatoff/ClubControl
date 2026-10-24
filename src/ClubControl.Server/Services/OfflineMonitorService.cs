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

namespace ClubControl.Server.Services;

/// <summary>
/// Catches PCs that went offline without a clean SignalR disconnect (cable pulled, PC crashed).
/// ClubHub.OnDisconnectedAsync handles the clean-disconnect case immediately; this is the backstop.
/// </summary>
public class OfflineMonitorService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;

    public OfflineMonitorService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync();
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                // A missed check should never take the whole server down; the next tick tries again.
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task CheckOnceAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();

        var now = DateTime.UtcNow;
        var onlinePcs = await db.Pcs.Where(p => p.IsOnline).ToListAsync();

        foreach (var pc in onlinePcs)
        {
            if (!OfflineDetector.IsOffline(pc.LastHeartbeatUtc, now))
                continue;

            pc.IsOnline = false;
            db.EventLogEntries.Add(new EventLogEntry
            {
                Type = EventType.PcDisconnected,
                PcId = pc.Id,
                Message = $"PC #{pc.Number?.ToString() ?? "?"} stopped sending heartbeats",
            });
        }

        if (onlinePcs.Count > 0)
            await db.SaveChangesAsync();
    }
}
