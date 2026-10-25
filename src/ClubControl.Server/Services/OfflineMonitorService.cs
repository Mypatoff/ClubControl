using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Hubs;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClubControl.Server.Services;

/// <summary>
/// Catches PCs that went offline without a clean SignalR disconnect (cable pulled, PC crashed).
/// ClubHub.OnDisconnectedAsync handles the clean-disconnect case immediately; this is the backstop.
/// </summary>
public class OfflineMonitorService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    // A check runs every 2s; logging every single failure while something stays broken would
    // flood the log forever. Log the first occurrence right away, then at most this often while
    // it keeps failing, plus one more line when it eventually recovers.
    private static readonly TimeSpan FailureLogInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OfflineMonitorService> _logger;

    private int _consecutiveFailures;
    private DateTime _lastLoggedFailureUtc = DateTime.MinValue;

    public OfflineMonitorService(IServiceScopeFactory scopeFactory, ILogger<OfflineMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync();

                if (_consecutiveFailures > 0)
                {
                    _logger.LogInformation(
                        "Offline monitor recovered after {FailureCount} consecutive failed checks.", _consecutiveFailures);
                    _consecutiveFailures = 0;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _consecutiveFailures++;
                var now = DateTime.UtcNow;

                if (_consecutiveFailures == 1 || now - _lastLoggedFailureUtc >= FailureLogInterval)
                {
                    _logger.LogError(ex,
                        "Offline monitor check failed ({FailureCount} consecutive failures so far); will keep retrying.",
                        _consecutiveFailures);
                    _lastLoggedFailureUtc = now;
                }

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

            // The socket may still look "connected" at the transport level even though the PC
            // stopped heartbeating (crash, cable pull, hung OS). Evict it now so no command is ever
            // addressed to this dead connection - see ClubHub.ForgetMachine.
            ClubHub.ForgetMachine(pc.MachineGuid);
        }

        if (onlinePcs.Count > 0)
            await db.SaveChangesAsync();
    }
}
